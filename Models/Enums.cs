namespace FxTradeConfirmation.Models;

public enum BuySell
{
    Buy,
    Sell
}

public enum CallPut
{
    Call,
    Put
}

public enum HedgeType
{
    No,
    Spot,
    Forward
}

/// <summary>
/// Defines how premium is quoted.
/// PctBase   = % of base currency notional   (premium amount in base ccy)
/// PipsQuote = pips in quote (price) currency (premium amount in quote ccy)
/// PctQuote  = % of quote currency notional   (premium amount in quote ccy)
/// PipsBase  = pips in base currency          (premium amount in base ccy)
/// </summary>
public enum PremiumStyle
{
    PctBase,
    PipsQuote,
    PctQuote,
    PipsBase
}

public enum PremiumDateType
{
    Spot,
    Forward
}
