namespace FxTradeConfirmation.Models;

/// <summary>
/// Holds all reference data loaded from the database.
/// Loaded once at startup, refreshed on reconnect.
/// </summary>
public class ReferenceData
{
    public List<string> Counterparts { get; set; } = [];
    public List<string> CurrencyPairs { get; set; } = [];
    public List<string> EmailAddresses { get; set; } = [];
    public List<string> Traders { get; set; } = ["P901GFT", "P901PEF", "P901XBK"];
    public List<string> Cuts { get; set; } = ["BFX", "BRL", "BUD", "CNY", "ECB", "KRW", "LON", "MEX", "NYC", "TKO", "TRY", "WAR", "WMR"];
    public List<string> MICs { get; set; } = ["BGCO", "BTFE", "FXOP", "GFSO", "GFSM", "XOFF", "TEFD"];
    public List<string> Brokers { get; set; } = ["BGC", "BLOOMBERG", "DRAX", "FENICS", "GFI", "ICAP", "LMR", "MAKOR", "SPECTRA", "TJM", "TULLETT"];
    public List<string> Portfolios { get; set; } = ["EMERG", "EUR", "EURNOK", "EURSEK", "FXSPOT_1", "FXSPOT_2", "FXSPOT_3", "FXSPOT_4", "FXSPOT_5", "FXSPOT_6", "JPY", "MAJORS", "PROP1", "PROP2", "PROP3", "PROP4", "USD"];
    public List<string> InvestmentDecisionIDs { get; set; } = [];
    public List<string> SalesNames { get; set; } = [];
    public List<string> ReportingEntities { get; set; } =
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
    public List<string> CalypsoBooks { get; set; } = ["FX22", "FX25", "FX51"];

    // Currency pair → Portfolio mapping
    public Dictionary<string, string> CurrencyToPortfolio { get; set; } = [];

    // User profile lookups (case-insensitive)
    /// <summary>userprofile.UserId → userprofile.FullName</summary>
    public Dictionary<string, string> UserIdToFullName { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>userprofile.FullName → userprofile.UserId</summary>
    public Dictionary<string, string> FullNameToUserId { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>userprofile.UserId → userprofile.ReportingEntityId</summary>
    public Dictionary<string, string> UserIdToReportingEntity { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>userprofile.UserId → userprofile.Mx3Id (used to resolve Trader from Windows login)</summary>
    public Dictionary<string, string> UserIdToMx3Id { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// stp_calypso_book_user.TraderId → stp_calypso_book_user.CalypsoBook
    /// Used to auto-select Calypso Book based on Environment.UserName.
    /// </summary>
    public Dictionary<string, string> TraderIdToCalypsoBook { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
