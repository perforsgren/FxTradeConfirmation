using FxSharedConfig;
using FxTradeConfirmation.Models;
using MySql.Data.MySqlClient;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Text.Json;

namespace FxTradeConfirmation.Services;

public class DatabaseService : IDatabaseService
{
    private const string Tag = "DatabaseService";

    private volatile string _connectionString = string.Empty;
    private volatile bool _connectionVerified;

    private static readonly string HolidayCachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FxTradeConfirmation", "holidays_cache.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    /// <summary>
    /// UNC path to a shared network cache file, derived from the same network
    /// settings root used by the rest of the application.
    /// Null if the path cannot be resolved.
    /// </summary>
    private static string? NetworkCachePath;

    /// <summary>
    /// Tracks the background network cache write so it can be awaited on shutdown.
    /// </summary>
    private static Task _pendingNetworkWrite = Task.CompletedTask;
    private static readonly object _networkWriteLock = new();

    public async Task InitializeAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                _connectionString = AppDbConfig.GetConnectionString("trade_stp");
            }
            catch (Exception ex)
            {
                FileLogger.Instance?.Error(Tag, "Failed to load connection string", ex);
            }

            NetworkCachePath = GetNetworkCachePath();
        });
    }

    /// <summary>
    /// Waits for any pending network cache write to complete.
    /// Call from <see cref="Application.Exit"/> or equivalent to avoid losing writes.
    /// </summary>
    public static void FlushPendingNetworkWrite()
    {
        try
        {
            Task pending;
            lock (_networkWriteLock)
            {
                pending = _pendingNetworkWrite;
            }
            pending.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            FileLogger.Instance?.Warn(Tag, $"FlushPendingNetworkWrite failed: {ex.Message}");
        }
    }

    public async Task<bool> TestConnectionAsync()
    {
        if (string.IsNullOrEmpty(_connectionString))
        {
            FileLogger.Instance?.Warn(Tag, "TestConnection skipped — no connection string.");
            return false;
        }

        try
        {
            await using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();
            _connectionVerified = true;
            FileLogger.Instance?.Info(Tag, "Connection test succeeded.");
            return true;
        }
        catch (MySqlException ex)
        {
            _connectionVerified = false;
            FileLogger.Instance?.Error(Tag, $"Connection test FAILED: {ex.Number}", ex);
            return false;
        }
    }

    public async Task<ReferenceData> LoadReferenceDataAsync()
    {
        if (!IsConnectionReady())
            return new ReferenceData();

        try
        {
            var counterparts = await QueryListAsync("SELECT CounterpartyCode FROM counterparty WHERE IsActive = 1 ORDER BY CounterpartyCode");
            var currencyPairs = await QueryListAsync("SELECT currencypair FROM currencypairs ORDER BY currencypair");
            var emailAddresses = await QueryListAsync("SELECT emailaddress FROM emailaddresses");
            var salesNames = await QueryListAsync("SELECT DISTINCT FullName FROM userprofile WHERE FullName IS NOT NULL AND IsActive = 1 ORDER BY FullName");
            var reportingEntities = await QueryListAsync("SELECT DISTINCT ReportingEntityId FROM userprofile WHERE ReportingEntityId IS NOT NULL AND IsActive = 1");
            var investmentDecIDs = await QueryListAsync("SELECT DISTINCT UserId FROM userprofile WHERE UserId IS NOT NULL AND IsActive = 1 ORDER BY UserId");

            var userIdToFullName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var userIdToReportingEntity = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var userIdToMx3Id = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var fullNameToUserId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var bloombergNameToSalesFullName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var currencyToPortfolio = new Dictionary<string, string>();
            var traderIdToCalypsoBook = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            await using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();

            // --- userprofile ---
            await using (var profileCmd = new MySqlCommand(
                "SELECT UserId, FullName, ReportingEntityId, Mx3Id, BloombergName FROM userprofile WHERE IsActive = 1 AND UserId IS NOT NULL AND FullName IS NOT NULL", conn))
            {
                await using var profileReader = await profileCmd.ExecuteReaderAsync();
                while (await profileReader.ReadAsync())
                {
                    var userId = profileReader.GetString(0);
                    var fullName = profileReader.GetString(1);
                    var reportingEnt = profileReader.IsDBNull(2) ? string.Empty : profileReader.GetString(2);
                    var mx3Id = profileReader.IsDBNull(3) ? string.Empty : profileReader.GetString(3);
                    var bbName = profileReader.IsDBNull(4) ? string.Empty : profileReader.GetString(4).Trim();

                    userIdToFullName.TryAdd(userId, fullName);
                    userIdToReportingEntity.TryAdd(userId, reportingEnt);
                    userIdToMx3Id.TryAdd(userId, mx3Id);
                    fullNameToUserId.TryAdd(fullName, userId);

                    if (!string.IsNullOrEmpty(bbName))
                        bloombergNameToSalesFullName.TryAdd(bbName, fullName);
                }
            }

            // --- ccypairportfoliorule ---
            await using (var cmd = new MySqlCommand(
                "SELECT CurrencyPair, PortfolioCode FROM ccypairportfoliorule WHERE IsActive = 1", conn))
            {
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    currencyToPortfolio[reader.GetString(0)] = reader.GetString(1);
            }

            // --- stp_calypso_book_user ---
            await using (var calypsoCmd = new MySqlCommand(
                "SELECT TraderId, CalypsoBook FROM stp_calypso_book_user WHERE IsActive = 1", conn))
            {
                await using var calypsoReader = await calypsoCmd.ExecuteReaderAsync();
                while (await calypsoReader.ReadAsync())
                    traderIdToCalypsoBook.TryAdd(calypsoReader.GetString(0), calypsoReader.GetString(1));
            }

            FileLogger.Instance?.Info(Tag, "Reference data loaded successfully.");

            return new ReferenceData
            {
                Counterparts = counterparts,
                CurrencyPairs = currencyPairs,
                EmailAddresses = emailAddresses,
                SalesNames = salesNames,
                ReportingEntities = reportingEntities,
                InvestmentDecisionIDs = investmentDecIDs,
                UserIdToFullName = userIdToFullName,
                UserIdToReportingEntity = userIdToReportingEntity,
                UserIdToMx3Id = userIdToMx3Id,
                FullNameToUserId = fullNameToUserId,
                CurrencyToPortfolio = currencyToPortfolio,
                TraderIdToCalypsoBook = traderIdToCalypsoBook,
                BloombergNameToSalesFullName = bloombergNameToSalesFullName,
            };
        }
        catch (MySqlException ex)
        {
            FileLogger.Instance?.Error(Tag, $"LoadReferenceData FAILED: {ex.Number}", ex);
            return new ReferenceData();
        }
    }

    public async Task<DataTable> LoadHolidaysAsync()
    {
        var dt = new DataTable();
        dt.Columns.Add("Market", typeof(string));
        dt.Columns.Add("HolidayDate", typeof(DateTime));

        const int maxAttempts = 5;
        const int retryDelayMs = 100;
        var sqlConnStr = GetHolidayConnectionString();

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                using var conn = new SqlConnection(sqlConnStr);
                await conn.OpenAsync(cts.Token);

                const string sql = "SELECT MARKET, HOLIDAY_DATE FROM Holiday WHERE HOLIDAY_DATE >= @startDate AND HOLIDAY_DATE <= @endDate";
                using var cmd = new SqlCommand(sql, conn);
                cmd.CommandTimeout = 15;
                cmd.Parameters.Add("@startDate", System.Data.SqlDbType.DateTime).Value = DateTime.Today;
                cmd.Parameters.Add("@endDate", System.Data.SqlDbType.DateTime).Value = DateTime.Today.AddYears(7);

                using var reader = await cmd.ExecuteReaderAsync(cts.Token);
                while (await reader.ReadAsync(cts.Token))
                {
                    var row = dt.NewRow();
                    row["Market"] = reader["MARKET"] as string;
                    row["HolidayDate"] = (DateTime)reader["HOLIDAY_DATE"];
                    dt.Rows.Add(row);
                }

                FileLogger.Instance?.Info(Tag, $"Holidays loaded from server: {dt.Rows.Count} rows (attempt {attempt}).");
                SaveHolidayCache(dt);
                return dt;
            }
            catch (OperationCanceledException)
            {
                FileLogger.Instance?.Warn(Tag, $"LoadHolidays attempt {attempt}/{maxAttempts} timed out.");
                if (attempt < maxAttempts)
                    await Task.Delay(retryDelayMs);
            }
            catch (SqlException ex)
            {
                FileLogger.Instance?.Error(Tag, $"LoadHolidays attempt {attempt}/{maxAttempts} failed (#{ex.Number})", ex);

                if (!IsTransientSqlError(ex))
                {
                    FileLogger.Instance?.Warn(Tag, $"Error #{ex.Number} is not transient — aborting retries.");
                    break;
                }

                if (attempt < maxAttempts)
                    await Task.Delay(retryDelayMs);
            }
        }

        // Fallback 1: local cache
        FileLogger.Instance?.Warn(Tag, "All retries failed — trying local cache.");
        var cached = LoadHolidayCache(HolidayCachePath);
        if (cached != null && cached.Rows.Count > 0)
        {
            FileLogger.Instance?.Warn(Tag, $"Using stale local holiday cache ({cached.Rows.Count} rows). Verify 'SqlConnections:ahs_holidays' in fx_appsettings.json.");
            return cached;
        }

        // Fallback 2: shared network cache
        if (!string.IsNullOrEmpty(NetworkCachePath))
        {
            FileLogger.Instance?.Warn(Tag, "No local cache — trying shared network cache.");
            var networkCached = LoadHolidayCache(NetworkCachePath);
            if (networkCached != null && networkCached.Rows.Count > 0)
            {
                FileLogger.Instance?.Warn(Tag, $"Using shared network holiday cache ({networkCached.Rows.Count} rows). Data may be stale.");
                // Promote to local cache so next startup is faster
                SaveLocalHolidayCache(networkCached);
                return networkCached;
            }
        }

        FileLogger.Instance?.Error(Tag, "No cache available and server unreachable. Holiday validation will be skipped.");
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
            FileLogger.Instance?.Error(Tag, $"GetPortfolio FAILED: {ex.Number}", ex);
            return string.Empty;
        }
    }

    // --- Holiday Cache ---

    /// <summary>
    /// Saves holiday data to both the local cache and (if configured) the
    /// shared network cache. Network write uses temp-file + move for atomicity.
    /// </summary>
    private static void SaveHolidayCache(DataTable dt)
    {
        var json = SerializeHolidayCache(dt);
        if (json == null)
            return;

        // Always save locally
        WriteJsonToFile(HolidayCachePath, json);

        // Best-effort save to network share (tracked for shutdown flush)
        if (!string.IsNullOrEmpty(NetworkCachePath))
        {
            lock (_networkWriteLock)
            {
                _pendingNetworkWrite = Task.Run(() =>
                {
                    try
                    {
                        WriteJsonToFile(NetworkCachePath, json);
                        FileLogger.Instance?.Info(Tag, "Network holiday cache updated.");
                    }
                    catch (Exception ex)
                    {
                        FileLogger.Instance?.Warn(Tag, $"Network holiday cache write failed (non-critical): {ex.Message}");
                    }
                });
            }
        }
    }

    /// <summary>
    /// Saves only to the local cache (used when promoting network data to local).
    /// </summary>
    private static void SaveLocalHolidayCache(DataTable dt)
    {
        var json = SerializeHolidayCache(dt);
        if (json != null)
            WriteJsonToFile(HolidayCachePath, json);
    }

    private static string? SerializeHolidayCache(DataTable dt)
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
            return JsonSerializer.Serialize(entries, JsonOptions);
        }
        catch (Exception ex)
        {
            FileLogger.Instance?.Error(Tag, "Holiday cache serialization failed", ex);
            return null;
        }
    }

    /// <summary>
    /// Writes JSON to a file using a unique temp-file + atomic move to avoid
    /// partial reads when multiple users write concurrently to the network share.
    /// </summary>
    private static void WriteJsonToFile(string path, string json)
    {
        try
        {
            var dir = Path.GetDirectoryName(path)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, path, overwrite: true);
            }
            finally
            {
                // Clean up temp file if Move failed and it's still on disk
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }
        catch (Exception ex)
        {
            FileLogger.Instance?.Error(Tag, $"Failed to write cache file '{path}'", ex);
        }
    }

    private static DataTable? LoadHolidayCache(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var entries = JsonSerializer.Deserialize<List<HolidayCacheEntry>>(
                File.ReadAllText(path), JsonOptions);

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
            FileLogger.Instance?.Error(Tag, $"Holiday cache load failed for '{path}'", ex);
            return null;
        }
    }

    // --- Helpers ---

    private static string? GetNetworkCachePath()
    {
        try
        {
            var settingsRoot = Path.Combine(AppPaths.Settings, "FxTradeConfirmation");
            return Path.Combine(settingsRoot, "holidays_cache.json");
        }
        catch
        {
            // AppPaths.Settings unavailable — network fallback disabled
            return null;
        }
    }

    private static string GetHolidayConnectionString()
    {
        const string fallback = "Data Source=10.5.85.33;Initial Catalog=AHS;Integrated Security=True;Connect Timeout=5;";

        string? raw = null;
        try
        {
            raw = AppDbConfig.GetRawConnectionString("SqlConnections:ahs_holidays");
        }
        catch (Exception ex)
        {
            FileLogger.Instance?.Error(Tag, "ahs_holidays key not found in config — using fallback.", ex);
            return fallback;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            FileLogger.Instance?.Warn(Tag, "ahs_holidays connection string is empty in config — using fallback.");
            return fallback;
        }

        if (!raw.Contains("Data Source", StringComparison.OrdinalIgnoreCase))
        {
            FileLogger.Instance?.Warn(Tag, $"ahs_holidays connection string appears invalid (missing 'Data Source'): '{raw}' — using fallback.");
            return fallback;
        }

        return raw;
    }

    private bool IsConnectionReady()
    {
        if (string.IsNullOrEmpty(_connectionString))
        {
            FileLogger.Instance?.Warn(Tag, "Skipped — no connection string.");
            return false;
        }

        if (!_connectionVerified)
        {
            // Silent — expected during normal startup before TestConnectionAsync has run
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

    private static bool IsTransientSqlError(SqlException ex)
    {
        // #53 = "server not found" — permanent at runtime, no point retrying.
        // Only retry on genuine transient conditions: timeout, busy server, reset connection.
        return ex.Number is
            -2 or     // timeout
            2 or      // unable to connect (port reachable, server busy)
            233 or    // connection closed by server
            1205 or   // deadlock
            10053 or  // connection reset
            10054 or  // connection reset by peer
            10060 or  // connection timed out
            10061;    // connection refused (port open, nothing listening)
        // Notably absent: 53 (server/host not found) — DNS/IP errors are permanent.
    }
}

internal sealed class HolidayCacheEntry
{
    public string Market { get; set; } = string.Empty;
    public DateTime HolidayDate { get; set; }
}
