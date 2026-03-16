using FxTradeConfirmation.Models;

namespace FxTradeConfirmation.Helpers;

public static class PremiumCalculator
{
    /// <summary>
    /// Calculate PremiumAmount from Premium (pips/pct) and Notional.
    /// </summary>
    public static decimal? CalculateAmount(decimal? premium, decimal? notional, PremiumStyle style)
    {
        if (!premium.HasValue || !notional.HasValue || notional.Value == 0) return null;

        return style switch
        {
            PremiumStyle.Pips => premium.Value * notional.Value / 10000m,
            PremiumStyle.Percent => premium.Value * notional.Value / 100m,
            _ => null
        };
    }

    /// <summary>
    /// Calculate Premium (pips/pct) from PremiumAmount and Notional.
    /// </summary>
    public static decimal? CalculatePremium(decimal? premiumAmount, decimal? notional, PremiumStyle style)
    {
        if (!premiumAmount.HasValue || !notional.HasValue || notional.Value == 0) return null;

        return style switch
        {
            PremiumStyle.Pips => premiumAmount.Value / notional.Value * 10000m,
            PremiumStyle.Percent => premiumAmount.Value / notional.Value * 100m,
            _ => null
        };
    }

    /// <summary>
    /// Apply Buy/Sell sign to premium amount.
    /// Buy (client buys) = negative (cost), Sell = positive (income).
    /// </summary>
    public static decimal ApplySign(decimal amount, BuySell buySell)
        => buySell == BuySell.Buy ? -Math.Abs(amount) : Math.Abs(amount);

    /// <summary>
    /// Determine hedge direction from option Buy/Sell and Call/Put.
    /// </summary>
    public static BuySell GetHedgeDirection(BuySell optionBuySell, CallPut callPut)
    {
        return (optionBuySell, callPut) switch
        {
            (BuySell.Buy, CallPut.Call) => BuySell.Sell,
            (BuySell.Buy, CallPut.Put) => BuySell.Buy,
            (BuySell.Sell, CallPut.Call) => BuySell.Buy,
            (BuySell.Sell, CallPut.Put) => BuySell.Sell,
            _ => BuySell.Buy
        };
    }
}
