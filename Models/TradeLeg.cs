namespace FxTradeConfirmation.Models;

/// <summary>
/// Represents one leg of an FX option trade.
/// This is the pure data model — no UI logic.
/// </summary>
public class TradeLeg
{
    // Option fields (rows 0–24)
    public string Counterpart { get; set; } = string.Empty;
    public string CurrencyPair { get; set; } = string.Empty;
    public BuySell BuySell { get; set; } = BuySell.Buy;
    public CallPut CallPut { get; set; } = CallPut.Call;
    public decimal? Strike { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public DateTime? SettlementDate { get; set; }
    public string Cut { get; set; } = "NYC";
    public decimal? Notional { get; set; }
    public string NotionalCurrency { get; set; } = string.Empty;
    public decimal? Premium { get; set; }
    public decimal? PremiumAmount { get; set; }
    public string PremiumCurrency { get; set; } = string.Empty;
    public DateTime? PremiumDate { get; set; }
    public PremiumStyle PremiumStyle { get; set; } = PremiumStyle.PipsQuote;
    public PremiumDateType PremiumDateType { get; set; } = PremiumDateType.Spot;
    public string PortfolioMX3 { get; set; } = string.Empty;
    public string Trader { get; set; } = "P901PEF";
    public string ExecutionTime { get; set; } = string.Empty;
    public string MIC { get; set; } = "XOFF";
    public string TVTIC { get; set; } = string.Empty;
    public string ISIN { get; set; } = string.Empty;
    public string Sales { get; set; } = string.Empty;
    public string InvestmentDecisionID { get; set; } = string.Empty;
    public string Broker { get; set; } = string.Empty;
    public decimal? Margin { get; set; }
    public string ReportingEntity { get; set; } = string.Empty;

    // Hedge fields (rows 25–34)
    public HedgeType Hedge { get; set; } = HedgeType.No;
    public BuySell HedgeBuySell { get; set; } = BuySell.Buy;
    public decimal? HedgeNotional { get; set; }
    public string HedgeNotionalCurrency { get; set; } = string.Empty;
    public decimal? HedgeRate { get; set; }
    public DateTime? HedgeSettlementDate { get; set; }
    public string HedgeTVTIC { get; set; } = string.Empty;
    public string HedgeUTI { get; set; } = string.Empty;
    public string HedgeISIN { get; set; } = string.Empty;
    public string BookCalypso { get; set; } = string.Empty;

    // Helper properties
    public string BaseCurrency => CurrencyPair.Length >= 3 ? CurrencyPair[..3] : string.Empty;
    public string QuoteCurrency => CurrencyPair.Length >= 6 ? CurrencyPair[3..6] : string.Empty;
}