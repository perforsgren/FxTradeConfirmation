using System.Data.SqlClient;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using FxTradeConfirmation.Models;
using FxSharedConfig;
using MySql.Data.MySqlClient;

namespace FxTradeConfirmation.Services;

public class DatabaseService : IDatabaseService
{
    private string _connectionString = string.Empty;
    private bool _connectionVerified;

    private static readonly string HolidayCachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FxTradeConfirmation", "holidays_cache.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public Task InitializeAsync()
    {
        try
        {
            _connectionString = AppDbConfig.GetConnectionString("trade_stp");
            Debug.WriteLine("[DatabaseService] Connection string loaded for trade_stp.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DatabaseService] Failed to load connection string: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public async Task<bool> TestConnectionAsync()
    {
        if (string.IsNullOrEmpty(_connectionString))
        {
            Debug.WriteLine("[DatabaseService] TestConnection skipped — no connection string.");
            return false;
        }

        try
        {
            await using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();
            _connectionVerified = true;
            Debug.WriteLine("[DatabaseService] Connection test succeeded.");
            return true;
        }
        catch (MySqlException ex)
        {
            _connectionVerified = false;
            Debug.WriteLine($"[DatabaseService] Connection test FAILED: {ex.Number} — {ex.Message}");
            return false;
        }
    }

    public async Task<ReferenceData> LoadReferenceDataAsync()
    {
        var data = new ReferenceData();

        if (!IsConnectionReady())
            return data;

        try
        {
            // Use separate connections per query to avoid "open DataReader" conflicts.
            // MySql Connector/NET does not support MARS — only one active reader per connection.
            data.Counterparts = await QueryListAsync("SELECT CounterpartyCode FROM counterparty WHERE IsActive = 1 ORDER BY CounterpartyCode");
            data.CurrencyPairs = await QueryListAsync("SELECT currencypair FROM currencypairs ORDER BY currencypair");
            data.EmailAddresses = await QueryListAsync("SELECT emailaddress FROM emailaddresses");
            data.SalesNames = await QueryListAsync("SELECT DISTINCT FullName FROM userprofile WHERE FullName IS NOT NULL AND IsActive = 1 ORDER BY FullName");
            data.ReportingEntities = await QueryListAsync("SELECT DISTINCT ReportingEntityId FROM userprofile WHERE ReportingEntityId IS NOT NULL AND IsActive = 1");
            data.InvestmentDecisionIDs = await QueryListAsync("SELECT DISTINCT UserId FROM userprofile WHERE UserId IS NOT NULL AND IsActive = 1 ORDER BY UserId");

            await using (var conn = new MySqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                await using var profileCmd = new MySqlCommand(
                    "SELECT UserId, FullName, ReportingEntityId, Mx3Id FROM userprofile WHERE IsActive = 1 AND UserId IS NOT NULL AND FullName IS NOT NULL", conn);
                await using var profileReader = await profileCmd.ExecuteReaderAsync();
                while (await profileReader.ReadAsync())
                {
                    var userId = profileReader.GetString(0);
                    var fullName = profileReader.GetString(1);
                    var reportingEntity = profileReader.IsDBNull(2) ? string.Empty : profileReader.GetString(2);
                    var mx3Id = profileReader.IsDBNull(3) ? string.Empty : profileReader.GetString(3);

                    data.UserIdToFullName.TryAdd(userId, fullName);
                    data.UserIdToReportingEntity.TryAdd(userId, reportingEntity);
                    data.UserIdToMx3Id.TryAdd(userId, mx3Id);
                    data.FullNameToUserId.TryAdd(fullName, userId);
                }
            }

            await using (var conn = new MySqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "SELECT CurrencyPair, PortfolioCode FROM ccypairportfoliorule WHERE IsActive = 1", conn);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    data.CurrencyToPortfolio[reader.GetString(0)] = reader.GetString(1);
            }

            await using (var conn = new MySqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                await using var calypsoCmd = new MySqlCommand(
                    "SELECT TraderId, CalypsoBook FROM stp_calypso_book_user WHERE IsActive = 1", conn);
                await using var calypsoReader = await calypsoCmd.ExecuteReaderAsync();
                while (await calypsoReader.ReadAsync())
                {
                    var traderId = calypsoReader.GetString(0);
                    var calypsoBook = calypsoReader.GetString(1);
                    data.TraderIdToCalypsoBook.TryAdd(traderId, calypsoBook);
                }
            }

            Debug.WriteLine("[DatabaseService] Reference data loaded successfully.");
        }
        catch (MySqlException ex)
        {
            Debug.WriteLine($"[DatabaseService] LoadReferenceData FAILED: {ex.Number} — {ex.Message}");
        }

        return data;
    }

    public async Task<DataTable> LoadHolidaysAsync()
    {
        var dt = new DataTable();
        dt.Columns.Add("Market", typeof(string));
        dt.Columns.Add("HolidayDate", typeof(DateTime));

        // Use IP directly to avoid DNS round-robin hitting the non-responsive address.
        // System.Data.SqlClient does not require TrustServerCertificate for internal servers.
        const int maxAttempts = 5;
        const string sqlConnStr = "Data Source=10.5.85.33;Initial Catalog=AHS;Integrated Security=True;Connect Timeout=5;";

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var conn = new SqlConnection(sqlConnStr);
                await conn.OpenAsync();

                const string sql = "SELECT MARKET, HOLIDAY_DATE FROM Holiday WHERE HOLIDAY_DATE >= @startDate AND HOLIDAY_DATE <= @endDate";
                using var cmd = new SqlCommand(sql, conn);
                cmd.CommandTimeout = 15;
                cmd.Parameters.Add("@startDate", System.Data.SqlDbType.DateTime).Value = DateTime.Today;
                cmd.Parameters.Add("@endDate", System.Data.SqlDbType.DateTime).Value = DateTime.Today.AddYears(7);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var row = dt.NewRow();
                    row["Market"] = reader["MARKET"] as string;
                    row["HolidayDate"] = (DateTime)reader["HOLIDAY_DATE"];
                    dt.Rows.Add(row);
                }

                Debug.WriteLine($"[DatabaseService] Holidays loaded from server: {dt.Rows.Count} rows (attempt {attempt}).");
                SaveHolidayCache(dt);
                return dt;
            }
            catch (SqlException ex)
            {
                Debug.WriteLine($"[DatabaseService] LoadHolidays attempt {attempt}/{maxAttempts} failed: {ex.Message}");
                if (attempt < maxAttempts)
                    await Task.Delay(100);
            }
        }

        Debug.WriteLine("[DatabaseService] All retries failed — trying local cache.");
        var cached = LoadHolidayCache();
        if (cached != null && cached.Rows.Count > 0)
        {
            Debug.WriteLine($"[DatabaseService] Holidays loaded from cache: {cached.Rows.Count} rows.");
            return cached;
        }

        Debug.WriteLine("[DatabaseService] No cache available. Holidays will be empty.");
        return dt;
    }

    public async Task<string> GetPortfolioForCurrencyPairAsync(string currencyPair)
    {
        if (!IsConnectionReady())
            return string.Empty;

        try
        {
            await using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT PortfolioCode FROM ccypairportfoliorule WHERE CurrencyPair = @ccypair AND IsActive = 1 LIMIT 1", conn);
            cmd.Parameters.AddWithValue("@ccypair", currencyPair);
            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString() ?? string.Empty;
        }
        catch (MySqlException ex)
        {
            Debug.WriteLine($"[DatabaseService] GetPortfolio FAILED: {ex.Number} — {ex.Message}");
            return string.Empty;
        }
    }

    // --- Holiday Cache ---

    private static void SaveHolidayCache(DataTable dt)
    {
        try
        {
            var entries = new List<HolidayCacheEntry>(dt.Rows.Count);
            foreach (DataRow row in dt.Rows)
            {
                entries.Add(new HolidayCacheEntry
                {
                    Market = (string)row["Market"],
                    HolidayDate = (DateTime)row["HolidayDate"]
                });
            }

            var dir = Path.GetDirectoryName(HolidayCachePath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(HolidayCachePath, JsonSerializer.Serialize(entries, JsonOptions));
            Debug.WriteLine($"[DatabaseService] Holiday cache saved: {entries.Count} entries → {HolidayCachePath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DatabaseService] Holiday cache save failed: {ex.Message}");
        }
    }

    private static DataTable? LoadHolidayCache()
    {
        try
        {
            if (!File.Exists(HolidayCachePath))
                return null;

            var entries = JsonSerializer.Deserialize<List<HolidayCacheEntry>>(
                File.ReadAllText(HolidayCachePath), JsonOptions);

            if (entries == null || entries.Count == 0)
                return null;

            var dt = new DataTable();
            dt.Columns.Add("Market", typeof(string));
            dt.Columns.Add("HolidayDate", typeof(DateTime));

            var today = DateTime.Now.Date;
            foreach (var entry in entries)
            {
                if (entry.HolidayDate >= today)
                {
                    var row = dt.NewRow();
                    row["Market"] = entry.Market;
                    row["HolidayDate"] = entry.HolidayDate;
                    dt.Rows.Add(row);
                }
            }

            return dt;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DatabaseService] Holiday cache load failed: {ex.Message}");
            return null;
        }
    }

    // --- Helpers ---

    private bool IsConnectionReady()
    {
        if (string.IsNullOrEmpty(_connectionString))
        {
            Debug.WriteLine("[DatabaseService] Skipped — no connection string.");
            return false;
        }

        if (!_connectionVerified)
        {
            Debug.WriteLine("[DatabaseService] Skipped — connection not verified.");
            return false;
        }

        return true;
    }

    private async Task<List<string>> QueryListAsync(string sql)
    {
        var list = new List<string>();
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(reader.GetString(0));
        return list;
    }
}

internal sealed class HolidayCacheEntry
{
    public string Market { get; set; } = string.Empty;
    public DateTime HolidayDate { get; set; }
}