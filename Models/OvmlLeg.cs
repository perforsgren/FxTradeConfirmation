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
    string Spot,
    string SenderName = "",
    /// <summary>
    /// True when the spot reference was extracted directly from the clipboard/input text.
    /// False when the spot was fetched from Bloomberg. Only a parsed spot should
    /// auto-populate Hedge Rate when the user selects a Hedge Type.
    /// </summary>
    bool SpotFromParsing = false
);
