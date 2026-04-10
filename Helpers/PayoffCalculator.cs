using System.Collections.Generic;
using System.Linq;
using FxTradeConfirmation.Models;
using FxTradeConfirmation.ViewModels;

namespace FxTradeConfirmation.Helpers;

/// <summary>
/// Computes the aggregated P&amp;L payoff at expiry for a collection of
/// vanilla FX option legs across a range of spot values.
/// </summary>
static class PayoffCalculator
{
    /// <summary>
    /// Calculates the total P&amp;L at expiry for all legs at evenly spaced
    /// spot values between <paramref name="minSpot"/> and <paramref name="maxSpot"/>.
    /// Legs with non-numeric strikes (e.g. "25D") are silently skipped.
    /// Premium is taken from <see cref="TradeLegViewModel.PremiumAmount"/> (the
    /// absolute currency amount) so it is on the same scale as the notional payoff.
    /// When <c>PremiumAmount</c> is null the leg is drawn without premium cost.
    /// </summary>
    public static IReadOnlyList<(double Spot, double PnL)> Calculate(
        IEnumerable<TradeLegViewModel> legs,
        double minSpot,
        double maxSpot,
        int points = 200)
    {
        var legList = legs
            .Where(l => l.Strike.HasValue && l.Notional.HasValue)
            .Select(l => (
                Strike: (double)l.Strike!.Value,
                Notional: (double)l.Notional!.Value,
                IsBuy: l.BuySell == BuySell.Buy,
                IsCall: l.CallPut == CallPut.Call,
                // PremiumAmount is already in the premium currency and correctly
                // signed (negative for Buy, positive for Sell). Use its absolute
                // value — the sign is handled by IsBuy below.
                PremiumAmount: l.PremiumAmount.HasValue ? Math.Abs((double)l.PremiumAmount.Value) : 0.0
            ))
            .ToList();

        var result = new (double Spot, double PnL)[points];
        double step = points > 1 ? (maxSpot - minSpot) / (points - 1) : 0;

        for (int i = 0; i < points; i++)
        {
            double spot = minSpot + step * i;
            double totalPnl = 0;

            foreach (var leg in legList)
            {
                // Intrinsic payoff in quote currency per unit of notional
                // For EURUSD: payoff = (S - K) × notional_EUR, result in USD
                double intrinsic = leg.IsCall
                    ? Math.Max(spot - leg.Strike, 0) * leg.Notional
                    : Math.Max(leg.Strike - spot, 0) * leg.Notional;

                // PremiumAmount is the total option cost in premium currency.
                // For a buyer:  net = intrinsic - premium paid
                // For a seller: net = premium received - intrinsic
                double netPnl = leg.IsBuy
                    ? intrinsic - leg.PremiumAmount
                    : leg.PremiumAmount - intrinsic;

                totalPnl += netPnl;
            }

            result[i] = (spot, totalPnl);
        }

        return result;
    }

    /// <summary>
    /// Determines a sensible spot range based on the strikes in the given legs,
    /// with a ±15% buffer so the full payoff shape (including the flat regions)
    /// is visible on both sides of every strike.
    /// Returns null when no legs have a numeric strike.
    /// </summary>
    public static (double Min, double Max)? GetSpotRange(IEnumerable<TradeLegViewModel> legs)
    {
        var strikes = legs
            .Where(l => l.Strike.HasValue)
            .Select(l => (double)l.Strike!.Value)
            .ToList();

        if (strikes.Count == 0)
            return null;

        double lo = strikes.Min();
        double hi = strikes.Max();

        // For a single strike or very tightly clustered strikes use the strike
        // value itself as the reference for the buffer so the payoff kink is
        // centred in the visible range.
        double mid = (lo + hi) / 2.0;
        double buffer = mid * 0.15;

        lo = Math.Min(lo, mid) - buffer;
        hi = Math.Max(hi, mid) + buffer;

        return (lo, hi);
    }

    /// <summary>
    /// Returns 3–5 evenly spaced "nice" tick values for the X axis that land on
    /// round numbers relative to the displayed spot range.
    /// </summary>
    public static IReadOnlyList<double> GetXTicks(double minSpot, double maxSpot, int targetTicks = 5)
    {
        double range = maxSpot - minSpot;
        if (range <= 0) return [];

        // Find the largest power-of-10 step that gives roughly targetTicks ticks
        double rawStep = range / targetTicks;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
        double niceStep = rawStep / mag switch
        {
            <= 1.0 => 1.0 * mag,
            <= 2.0 => 2.0 * mag,
            <= 2.5 => 2.5 * mag,
            <= 5.0 => 5.0 * mag,
            _ => 10.0 * mag
        };

        // Snap the first tick to a multiple of niceStep above minSpot
        double first = Math.Ceiling(minSpot / niceStep) * niceStep;

        var ticks = new List<double>();
        for (double t = first; t <= maxSpot + niceStep * 0.001; t += niceStep)
            ticks.Add(Math.Round(t, 10));   // eliminate floating-point drift

        return ticks;
    }
}
