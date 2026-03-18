using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using FxTradeConfirmation.Models;
using MySql.Data.MySqlClient;
using System.IO;

namespace FxTradeConfirmation.Services;

public class DatabaseService : IDatabaseService
{
    private string _connectionString = string.Empty;
    private bool _connectionVerified;

    private static readonly string CredentialsPath =
        @"L:\FXFI\FXFI Trading\FX & Com Trading\Options\C#\Settings\STP\Database.txt";

    public async Task InitializeAsync()
    {
        if (!File.Exists(CredentialsPath))
        {
            Debug.WriteLine($"[DatabaseService] Credentials file not found: {CredentialsPath}");
            return;
        }

        var lines = await File.ReadAllLinesAsync(CredentialsPath);
        if (lines.Length >= 2)
        {
            var user = lines[0].Trim();
            var pass = lines[1].Trim();
            _connectionString = $"Server=srv78506;Port=3306;Database=fxoptions;Uid={user};Pwd={pass};Connection Timeout=15;";
            Debug.WriteLine($"[DatabaseService] Connection string built for user '{user}'.");
        }
        else
        {
            Debug.WriteLine($"[DatabaseService] Credentials file has fewer than 2 lines.");
        }
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

            // TEMPORARY: print actual column names of userlist to debug
            await DebugPrintColumnsAsync(conn, "userlist");

            data.Counterparts = await QueryListAsync(conn, "SELECT shortname FROM counterparts ORDER BY shortname");
            data.CurrencyPairs = await QueryListAsync(conn, "SELECT currencypair FROM currencypairs ORDER BY currencypair");
            data.EmailAddresses = await QueryListAsync(conn, "SELECT emailaddress FROM emailaddresses");
            data.SalesNames = await QueryListAsync(conn, "SELECT DISTINCT Sales FROM userlist WHERE Sales IS NOT NULL ORDER BY Sales");
            data.ReportingEntities = await QueryListAsync(conn, "SELECT DISTINCT ReportingEntity FROM userlist WHERE ReportingEntity IS NOT NULL");
            data.InvestmentDecisionIDs = await QueryListAsync(conn, "SELECT DISTINCT InvDecID FROM userlist WHERE InvDecID IS NOT NULL");

            await using var cmd = new MySqlCommand("SELECT currencypair, portfolio FROM currencytoportfolio", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                data.CurrencyToPortfolio[reader.GetString(0)] = reader.GetString(1);

            Debug.WriteLine("[DatabaseService] Reference data loaded successfully.");
        }
        catch (MySqlException ex)
        {
            // NOTE: do NOT reset _connectionVerified here — a bad query is not a lost connection
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
            // Holidays are on the SQL Server (AHS) database, same as the legacy HolidayDAO
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
                "SELECT portfolio FROM currencytoportfolio WHERE currencypair = @ccypair", conn);
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
                "SELECT DISTINCT Sales FROM userlist WHERE PID = @pid", conn);
            cmd.Parameters.AddWithValue("@pid", username);
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
                "SELECT DISTINCT ReportingEntity FROM userlist WHERE Sales = @sales", conn);
            cmd.Parameters.AddWithValue("@sales", salesName);
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
                "SELECT DISTINCT InvDecID FROM userlist WHERE PID = @pid", conn);
            cmd.Parameters.AddWithValue("@pid", username);
            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString() ?? string.Empty;
        }
        catch (MySqlException ex)
        {
            Debug.WriteLine($"[DatabaseService] GetInvestmentDecisionId FAILED: {ex.Number} — {ex.Message}");
            return string.Empty;
        }
    }

    public async Task SaveTradeAsync(IReadOnlyList<TradeLeg> legs)
    {
        if (string.IsNullOrEmpty(_connectionString))
            throw new InvalidOperationException("Database is not connected. Cannot save trade.");

        var now = DateTime.UtcNow;
        var tradeIdBase = now.ToString("yyyyMMddHHmmssfff");
        var isAdmin = IsAdminUser();

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            for (int i = 0; i < legs.Count; i++)
            {
                var leg = legs[i];
                var legNum = i + 1;

                var optTradeId = $"{tradeIdBase}_{leg.CurrencyPair}_O{legNum}";
                var invertedBuySell = leg.BuySell == BuySell.Buy ? "Sell" : "Buy";
                var premiumAbs = leg.PremiumAmount.HasValue ? Math.Abs(leg.PremiumAmount.Value) : 0m;

                await using var optCmd = new MySqlCommand(@"
                    INSERT INTO opt (TradeID, Counterpart, CurrencyPair, BuySell, CallPut, Strike,
                        ExpiryDate, SettlementDate, Cut, Notional, NotionalCurrency, Premium,
                        PremiumCurrency, PremiumDate, PortfolioMX3, Trader, ExecutionTime, MIC,
                        TVTIC, ISIN, InvID, ReportingEntity, Broker, StatusMX3, ImportedBy,
                        Margin, BookedBy, TimeReceived)
                    VALUES (@TradeID, @Counterpart, @CurrencyPair, @BuySell, @CallPut, @Strike,
                        @ExpiryDate, @SettlementDate, @Cut, @Notional, @NotionalCurrency, @Premium,
                        @PremiumCurrency, @PremiumDate, @PortfolioMX3, @Trader, @ExecutionTime, @MIC,
                        @TVTIC, @ISIN, @InvID, @ReportingEntity, @Broker, @StatusMX3, @ImportedBy,
                        @Margin, @BookedBy, @TimeReceived)", conn, transaction);

                optCmd.Parameters.AddWithValue("@TradeID", optTradeId);
                optCmd.Parameters.AddWithValue("@Counterpart", leg.Counterpart);
                optCmd.Parameters.AddWithValue("@CurrencyPair", leg.CurrencyPair);
                optCmd.Parameters.AddWithValue("@BuySell", invertedBuySell);
                optCmd.Parameters.AddWithValue("@CallPut", leg.CallPut.ToString());
                optCmd.Parameters.AddWithValue("@Strike", leg.Strike ?? 0m);
                optCmd.Parameters.AddWithValue("@ExpiryDate", leg.ExpiryDate?.ToString("yyyy-MM-dd") ?? "");
                optCmd.Parameters.AddWithValue("@SettlementDate", leg.SettlementDate?.ToString("yyyy-MM-dd") ?? "");
                optCmd.Parameters.AddWithValue("@Cut", leg.Cut);
                optCmd.Parameters.AddWithValue("@Notional", leg.Notional ?? 0m);
                optCmd.Parameters.AddWithValue("@NotionalCurrency", leg.NotionalCurrency);
                optCmd.Parameters.AddWithValue("@Premium", premiumAbs);
                optCmd.Parameters.AddWithValue("@PremiumCurrency", leg.PremiumCurrency);
                optCmd.Parameters.AddWithValue("@PremiumDate", leg.PremiumDate?.ToString("yyyy-MM-dd") ?? "");
                optCmd.Parameters.AddWithValue("@PortfolioMX3", leg.PortfolioMX3);
                optCmd.Parameters.AddWithValue("@Trader", leg.Trader);
                optCmd.Parameters.AddWithValue("@ExecutionTime", leg.ExecutionTime);
                optCmd.Parameters.AddWithValue("@MIC", leg.MIC);
                optCmd.Parameters.AddWithValue("@TVTIC", leg.TVTIC);
                optCmd.Parameters.AddWithValue("@ISIN", leg.ISIN);
                optCmd.Parameters.AddWithValue("@InvID", leg.InvestmentDecisionID);
                optCmd.Parameters.AddWithValue("@ReportingEntity", leg.ReportingEntity);
                optCmd.Parameters.AddWithValue("@Broker", leg.Broker);
                optCmd.Parameters.AddWithValue("@StatusMX3", "");
                optCmd.Parameters.AddWithValue("@ImportedBy", Environment.UserName);
                optCmd.Parameters.AddWithValue("@Margin", leg.Margin ?? 0m);
                optCmd.Parameters.AddWithValue("@BookedBy", Environment.UserName);
                optCmd.Parameters.AddWithValue("@TimeReceived", now.ToString("yyyy-MM-dd HH:mm:ss"));

                await optCmd.ExecuteNonQueryAsync();

                if (leg.Hedge != HedgeType.No)
                {
                    var hedgeTradeId = $"{tradeIdBase}_{leg.CurrencyPair}_H{legNum}";
                    var invertedHedgeBuySell = leg.HedgeBuySell == BuySell.Buy ? "Sell" : "Buy";

                    await using var hedgeCmd = new MySqlCommand(@"
                        INSERT INTO hedge (TradeID, Counterpart, CurrencyPair, BuySell, Notional,
                            NotionalCurrency, HedgeRate, HedgeType, SettlementDate, PortfolioMX3,
                            BookCalypso, Trader, ExecutionTime, MIC, TVTIC, UTI, ISIN, InvID,
                            StatusMX3, StatusCalypso, STP, ImportedBy, BookedBy, TimeReceived)
                        VALUES (@TradeID, @Counterpart, @CurrencyPair, @BuySell, @Notional,
                            @NotionalCurrency, @HedgeRate, @HedgeType, @SettlementDate, @PortfolioMX3,
                            @BookCalypso, @Trader, @ExecutionTime, @MIC, @TVTIC, @UTI, @ISIN, @InvID,
                            @StatusMX3, @StatusCalypso, @STP, @ImportedBy, @BookedBy, @TimeReceived)", conn, transaction);

                    hedgeCmd.Parameters.AddWithValue("@TradeID", hedgeTradeId);
                    hedgeCmd.Parameters.AddWithValue("@Counterpart", leg.Counterpart);
                    hedgeCmd.Parameters.AddWithValue("@CurrencyPair", leg.CurrencyPair);
                    hedgeCmd.Parameters.AddWithValue("@BuySell", invertedHedgeBuySell);
                    hedgeCmd.Parameters.AddWithValue("@Notional", leg.HedgeNotional ?? 0m);
                    hedgeCmd.Parameters.AddWithValue("@NotionalCurrency", leg.HedgeNotionalCurrency);
                    hedgeCmd.Parameters.AddWithValue("@HedgeRate", leg.HedgeRate ?? 0m);
                    hedgeCmd.Parameters.AddWithValue("@HedgeType", leg.Hedge.ToString());
                    hedgeCmd.Parameters.AddWithValue("@SettlementDate", leg.HedgeSettlementDate?.ToString("yyyy-MM-dd") ?? "");
                    hedgeCmd.Parameters.AddWithValue("@PortfolioMX3", leg.PortfolioMX3);
                    hedgeCmd.Parameters.AddWithValue("@BookCalypso", leg.BookCalypso);
                    hedgeCmd.Parameters.AddWithValue("@Trader", leg.Trader);
                    hedgeCmd.Parameters.AddWithValue("@ExecutionTime", leg.ExecutionTime);
                    hedgeCmd.Parameters.AddWithValue("@MIC", leg.MIC);
                    hedgeCmd.Parameters.AddWithValue("@TVTIC", leg.HedgeTVTIC);
                    hedgeCmd.Parameters.AddWithValue("@UTI", leg.HedgeUTI);
                    hedgeCmd.Parameters.AddWithValue("@ISIN", leg.HedgeISIN);
                    hedgeCmd.Parameters.AddWithValue("@InvID", leg.InvestmentDecisionID);
                    hedgeCmd.Parameters.AddWithValue("@StatusMX3", "");
                    hedgeCmd.Parameters.AddWithValue("@StatusCalypso", "");
                    hedgeCmd.Parameters.AddWithValue("@STP", isAdmin ? "false" : "true");
                    hedgeCmd.Parameters.AddWithValue("@ImportedBy", Environment.UserName);
                    hedgeCmd.Parameters.AddWithValue("@BookedBy", Environment.UserName);
                    hedgeCmd.Parameters.AddWithValue("@TimeReceived", now.ToString("yyyy-MM-dd HH:mm:ss"));

                    await hedgeCmd.ExecuteNonQueryAsync();
                }
            }

            await using var updateCmd = new MySqlCommand(
                "UPDATE lastupdate SET Time = @Time, UpdateType = @UpdateType WHERE Type = @Type", conn, transaction);
            updateCmd.Parameters.AddWithValue("@Time", now.ToString("yyyy-MM-dd HH:mm:ss"));
            updateCmd.Parameters.AddWithValue("@UpdateType", "Trade");
            updateCmd.Parameters.AddWithValue("@Type", "Option");
            await updateCmd.ExecuteNonQueryAsync();

            await transaction.CommitAsync();
            Debug.WriteLine("[DatabaseService] Trade saved successfully.");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
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

    private static bool IsAdminUser()
    {
        var user = Environment.UserName.ToUpperInvariant();
        return user is "P901PEF" or "P901MGU";
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

    /// <summary>
    /// Temporary helper — prints actual column names of a table to the Debug output.
    /// Remove once column names are confirmed.
    /// </summary>
    private static async Task DebugPrintColumnsAsync(MySqlConnection conn, string tableName)
    {
        await using var cmd = new MySqlCommand(
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table ORDER BY ORDINAL_POSITION",
            conn);
        cmd.Parameters.AddWithValue("@table", tableName);
        await using var reader = await cmd.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(0));
        Debug.WriteLine($"[DatabaseService] Columns in '{tableName}': {string.Join(", ", columns)}");
    }
}