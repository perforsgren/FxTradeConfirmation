namespace FxTradeConfirmation.Models;

/// <summary>
/// Holds all reference data loaded from the database.
/// Immutable after construction: all collections are exposed as read-only
/// interfaces to prevent mutation of a live instance from any thread.
/// DatabaseService builds a fresh instance locally and publishes it atomically
/// via Dispatcher.Invoke — callers always see a fully consistent snapshot.
/// </summary>
public class ReferenceData
{
    public IReadOnlyList<string> Counterparts { get; init; } = [];
    public IReadOnlyList<string> CurrencyPairs { get; init; } = [];
    public IReadOnlyList<string> EmailAddresses { get; init; } = [];
    public IReadOnlyList<string> Traders { get; init; } = ["P901GFT", "P901PEF", "P901XBK"];
    public IReadOnlyList<string> Cuts { get; init; } = ["BFX", "BRL", "BUD", "CNY", "ECB", "KRW", "LON", "MEX", "NYC", "TKO", "TRY", "WAR", "WMR"];
    public IReadOnlyList<string> MICs { get; init; } = ["BGCO", "BTFE", "FXOP", "GFSO", "GFSM", "XOFF", "TEFD"];
    public IReadOnlyList<string> Brokers { get; init; } = ["BGC", "BLOOMBERG", "DRAX", "FENICS", "GFI", "ICAP", "LMR", "MAKOR", "SPECTRA", "TJM", "TULLETT"];
    public IReadOnlyList<string> Portfolios { get; init; } = ["EMERG", "EUR", "EURNOK", "EURSEK", "FXSPOT_1", "FXSPOT_2", "FXSPOT_3", "FXSPOT_4", "FXSPOT_5", "FXSPOT_6", "JPY", "MAJORS", "PROP1", "PROP2", "PROP3", "PROP4", "USD"];
    public IReadOnlyList<string> InvestmentDecisionIDs { get; init; } = [];
    public IReadOnlyList<string> SalesNames { get; init; } = [];
    public IReadOnlyList<string> ReportingEntities { get; init; } =
    [
        "CORPORATE SALES FINLAND",
        "CORPORATE SALES GOTHENBURG",
        "CORPORATE SALES MALMO",
        "CORPORATE SALES NORWAY",
        "CORPORATE SALES STOCKHOLM",
        "FX INSTITUTIONAL CLIENTS",
        "FX VOLATILITY",
    ];

    // TODO: Replace hardcoded list with DB lookup from trade_stp.stp_calypso_book (or similar reference table)
    public IReadOnlyList<string> CalypsoBooks { get; init; } = ["FX22", "FX25", "FX51"];

    // Currency pair → Portfolio mapping
    public IReadOnlyDictionary<string, string> CurrencyToPortfolio { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // User profile lookups (case-insensitive)
    /// <summary>userprofile.UserId → userprofile.FullName</summary>
    public IReadOnlyDictionary<string, string> UserIdToFullName { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>userprofile.FullName → userprofile.UserId</summary>
    public IReadOnlyDictionary<string, string> FullNameToUserId { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>userprofile.UserId → userprofile.ReportingEntityId</summary>
    public IReadOnlyDictionary<string, string> UserIdToReportingEntity { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>userprofile.UserId → userprofile.Mx3Id (used to resolve Trader from Windows login)</summary>
    public IReadOnlyDictionary<string, string> UserIdToMx3Id { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// stp_calypso_book_user.TraderId → stp_calypso_book_user.CalypsoBook
    /// Used to auto-select Calypso Book based on Environment.UserName.
    /// </summary>
    public IReadOnlyDictionary<string, string> TraderIdToCalypsoBook { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
