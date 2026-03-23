using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using FxTradeConfirmation.Models;
using FxSharedConfig;
using MySql.Data.MySqlClient;

namespace FxTradeConfirmation.Services;

public class DatabaseService : IDatabaseService
{
    private string _connectionString = string.Empty;
    private bool _connectionVerified;

    public Task InitializeAsync()
    {
        try
        {
            _connectionString = AppDbConfig.GetConnectionString("trade_stp");
            Debug.WriteLine($"[DatabaseService] Connection string loaded for trade_stp.");
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
            await using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();

            data.Counterparts = await QueryListAsync(conn, "SELECT CounterpartyCode FROM counterparty WHERE IsActive = 1 ORDER BY CounterpartyCode");
            data.CurrencyPairs = await QueryListAsync(conn, "SELECT currencypair FROM currencypairs ORDER BY currencypair");
            data.EmailAddresses = await QueryListAsync(conn, "SELECT emailaddress FROM emailaddresses");
            data.SalesNames = await QueryListAsync(conn, "SELECT DISTINCT FullName FROM userprofile WHERE FullName IS NOT NULL AND IsActive = 1 ORDER BY FullName");
            data.ReportingEntities = await QueryListAsync(conn, "SELECT DISTINCT ReportingEntityId FROM userprofile WHERE ReportingEntityId IS NOT NULL AND IsActive = 1");
            data.InvestmentDecisionIDs = await QueryListAsync(conn, "SELECT DISTINCT UserId FROM userprofile WHERE UserId IS NOT NULL AND IsActive = 1 ORDER BY UserId");

            // Load user profile lookup maps: UserId ↔ FullName, UserId → ReportingEntityId, UserId → Mx3Id
            await using var profileCmd = new MySqlCommand(
                "SELECT UserId, FullName, ReportingEntityId, Mx3Id FROM userprofile WHERE IsActive = 1 AND UserId IS NOT NULL AND FullName IS NOT NULL", conn);
            await using var profileReader = await profileCmd.ExecuteReaderAsync();
            while (await profileReader.ReadAsync())
            {
                var userId = profileReader.GetString(0);
                var fullName = profileReader.GetString(1);
                var reportingEntity = profileReader.IsDBNull(2) ? string.Empty : profileReader.GetString(2);
                var mx3Id = profileReader.IsDBNull(3) ? string.Empty : profileReader.GetString(3);

                // UserId is unique — safe to TryAdd
                data.UserIdToFullName.TryAdd(userId, fullName);
                data.UserIdToReportingEntity.TryAdd(userId, reportingEntity);
                data.UserIdToMx3Id.TryAdd(userId, mx3Id);
                // FullName may not be unique — first match wins for reverse lookup
                data.FullNameToUserId.TryAdd(fullName, userId);
            }
            await profileReader.CloseAsync();

            await using var cmd = new MySqlCommand(
                "SELECT CurrencyPair, PortfolioCode FROM ccypairportfoliorule WHERE IsActive = 1", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                data.CurrencyToPortfolio[reader.GetString(0)] = reader.GetString(1);

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

        try
        {
            var sqlConnStr = "Data Source=AHSKvant-prod-db;Initial Catalog=AHS;Integrated Security=True;Connect Timeout=15;";
            using var conn = new SqlConnection(sqlConnStr);
            await conn.OpenAsync();

            var sql = $"SELECT MARKET, HOLIDAY_DATE FROM Holiday WHERE HOLIDAY_DATE >= '{DateTime.Now:yyyy-MM-dd}' AND HOLIDAY_DATE <= '{DateTime.Now.AddYears(7):yyyy-MM-dd}'";
            using var cmd = new SqlCommand(sql, conn);
            cmd.CommandTimeout = 15;

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = dt.NewRow();
                row["Market"] = reader.GetString(0);
                row["HolidayDate"] = reader.GetDateTime(1);
                dt.Rows.Add(row);
            }

            Debug.WriteLine($"[DatabaseService] Holidays loaded: {dt.Rows.Count} rows.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DatabaseService] LoadHolidays FAILED: {ex.Message}");
        }

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

    public async Task<string> GetSalesNameAsync(string username)
    {
        if (!IsConnectionReady())
            return string.Empty;

        try
        {
            await using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT FullName FROM userprofile WHERE UserId = @pid AND IsActive = 1 LIMIT 1", conn);
            cmd.Parameters.AddWithValue("@pid", username.ToUpperInvariant());
            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString() ?? string.Empty;
        }
        catch (MySqlException ex)
        {
            Debug.WriteLine($"[DatabaseService] GetSalesName FAILED: {ex.Number} — {ex.Message}");
            return string.Empty;
        }
    }

    public async Task<string> GetReportingEntityAsync(string salesName)
    {
        if (!IsConnectionReady())
            return string.Empty;

        try
        {
            await using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT ReportingEntityId FROM userprofile WHERE FullName = @fullname AND IsActive = 1 LIMIT 1", conn);
            cmd.Parameters.AddWithValue("@fullname", salesName);
            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString() ?? string.Empty;
        }
        catch (MySqlException ex)
        {
            Debug.WriteLine($"[DatabaseService] GetReportingEntity FAILED: {ex.Number} — {ex.Message}");
            return string.Empty;
        }
    }

    public async Task<string> GetInvestmentDecisionIdAsync(string username)
    {
        if (!IsConnectionReady())
            return string.Empty;

        try
        {
            await using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT Mx3Id FROM userprofile WHERE UserId = @pid AND IsActive = 1 LIMIT 1", conn);
            cmd.Parameters.AddWithValue("@pid", username.ToUpperInvariant());
            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString() ?? string.Empty;
        }
        catch (MySqlException ex)
        {
            Debug.WriteLine($"[DatabaseService] GetInvestmentDecisionId FAILED: {ex.Number} — {ex.Message}");
            return string.Empty;
        }
    }

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

    private static async Task<List<string>> QueryListAsync(MySqlConnection conn, string sql)
    {
        var list = new List<string>();
        await using var cmd = new MySqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(reader.GetString(0));
        return list;
    }
}