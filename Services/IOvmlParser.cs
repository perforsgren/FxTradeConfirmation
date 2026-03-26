using FxTradeConfirmation.Models;

namespace FxTradeConfirmation.Services;

/// <summary>
/// Parses a free-text FX option request into an OVML string and a list of legs.
/// Returns false if parsing failed; the caller should fall back to the next parser.
/// </summary>
public interface IOvmlParser
{
    /// <summary>
    /// Attempts to parse <paramref name="input"/> into an OVML string and legs.
    /// </summary>
    /// <returns>True if parsing produced a non-empty result.</returns>
    bool TryParse(string input, out string ovml, out IReadOnlyList<OvmlLeg> legs);

    /// <summary>
    /// Async version of <see cref="TryParse"/>. Default implementation delegates to the sync version.
    /// </summary>
    Task<(bool Success, string Ovml, IReadOnlyList<OvmlLeg> Legs)> TryParseAsync(
        string input, CancellationToken ct = default)
    {
        var success = TryParse(input, out var ovml, out var legs);
        return Task.FromResult((success, ovml, legs));
    }
}