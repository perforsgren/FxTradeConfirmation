namespace FxTradeConfirmation.Models;

public enum BuySell
{
    Buy = 0,
    Sell = 1
}

public enum CallPut
{
    Call = 0,
    Put = 1
}

public enum HedgeType
{
    No = 0,
    Spot = 1,
    Forward = 2
}

/// <summary>
/// Defines how premium is quoted.
/// PctBase   = % of base currency notional   (premium amount in base ccy)
/// PipsQuote = pips in quote (price) currency (premium amount in quote ccy)
/// PctQuote  = % of quote currency notional   (premium amount in quote ccy)
/// </summary>
public enum PremiumStyle
{
    PctBase = 0,
    PipsQuote = 1,
    PctQuote = 2
}

public enum PremiumDateType
{
    Spot = 0,
    Forward = 1
}
