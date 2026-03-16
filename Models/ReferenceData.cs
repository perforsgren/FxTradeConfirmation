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
    public List<string> ReportingEntities { get; set; } = [];

    // Currency pair → Portfolio mapping
    public Dictionary<string, string> CurrencyToPortfolio { get; set; } = [];
}
