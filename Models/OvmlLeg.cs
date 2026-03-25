namespace FxTradeConfirmation.Models;

/// <summary>
/// One parsed leg from a Bloomberg option request, produced by either
/// the regex parser (AP3) or the AI parser (OvmlBuilder).
/// </summary>
public sealed record OvmlLeg(
    string Pair,
    string BuySell,
    string PutCall,
    string Strike,
    long Notional,
    string Expiry,
    string Spot
);