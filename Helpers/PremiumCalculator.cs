using FxTradeConfirmation.Models;

namespace FxTradeConfirmation.Helpers;

public static class PremiumCalculator
{
    /// <summary>
    /// Strips separators (/, -, _) from a currency pair before inspection.
    /// </summary>
    private static string Normalize(string currencyPair) =>
        currencyPair.Replace("/", string.Empty)
                    .Replace("-", string.Empty)
                    .Replace("_", string.Empty);

    /// <summary>
    /// Returns the pip divisor for the given currency pair.
    /// JPY as quote currency uses 100; all other pairs use 10 000.
    /// </summary>
    public static decimal PipDivisor(string currencyPair)
    {
        string normalized = Normalize(currencyPair);
        return normalized.Length >= 6 && normalized[3..6] == "JPY" ? 100m : 10_000m;
    }

    /// <summary>
    /// Calculate PremiumAmount from Premium (pips/pct) and Notional.
    /// Strike is needed for cross-currency pips calculations.
    /// currencyPair is required for PipsQuote to apply the correct pip divisor.
    /// </summary>
    public static decimal? CalculateAmount(decimal? premium, decimal? notional, PremiumStyle style,
        decimal? strike = null, string currencyPair = "")
    {
        if (!premium.HasValue || !notional.HasValue || notional.Value == 0) return null;

        return style switch
        {
            // % of base ccy notional → amount = premium% * notional
            PremiumStyle.PctBase => premium.Value * notional.Value / 100m,

            // Quote-ccy pips → amount = premium * notional / pip divisor (100 for JPY, 10 000 otherwise)
            PremiumStyle.PipsQuote => premium.Value * notional.Value / PipDivisor(currencyPair),

            // % of quote ccy notional → need strike to convert notional to quote ccy
            // quote notional = notional * strike, amount = premium% * quote notional
            PremiumStyle.PctQuote => strike.HasValue && strike.Value != 0
                ? premium.Value * notional.Value * strike.Value / 100m
                : null,

            _ => null
        };
    }

    /// <summary>
    /// Calculate Premium (pips/pct) from PremiumAmount and Notional.
    /// Strike is needed for cross-currency pips calculations.
    /// currencyPair is required for PipsQuote to apply the correct pip divisor.
    /// </summary>
    public static decimal? CalculatePremium(decimal? premiumAmount, decimal? notional, PremiumStyle style,
        decimal? strike = null, string currencyPair = "")
    {
        if (!premiumAmount.HasValue || !notional.HasValue || notional.Value == 0) return null;

        return style switch
        {
            PremiumStyle.PctBase => premiumAmount.Value / notional.Value * 100m,

            PremiumStyle.PipsQuote => premiumAmount.Value / notional.Value * PipDivisor(currencyPair),

            PremiumStyle.PctQuote => strike.HasValue && strike.Value != 0
                ? premiumAmount.Value / (notional.Value * strike.Value) * 100m
                : null,

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
