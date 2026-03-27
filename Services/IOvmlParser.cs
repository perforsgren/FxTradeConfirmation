using FxTradeConfirmation.Models;

namespace FxTradeConfirmation.Services;

/// <summary>
/// Parses a free-text FX option request into an OVML string and a list of legs.
/// Returns false if parsing failed; the caller should fall back to the next parser.
/// </summary>
public interface IOvmlParser
{
    /// <summary>
    /// Synchronous parse. Suitable for fast, CPU-bound implementations (e.g. regex).
    /// AI-backed implementations should throw <see cref="NotSupportedException"/> and
    /// direct callers to <see cref="TryParseAsync"/> instead.
    /// </summary>
    bool TryParse(string input, out string ovml, out IReadOnlyList<OvmlLeg> legs);

    /// <summary>
    /// Async parse. Preferred entry point for all implementations that perform I/O.
    /// The default delegates to the synchronous version for CPU-bound parsers.
    /// </summary>
    Task<(bool Success, string Ovml, IReadOnlyList<OvmlLeg> Legs)> TryParseAsync(
        string input, CancellationToken ct = default)
    {
        var success = TryParse(input, out var ovml, out var legs);
        return Task.FromResult((success, ovml, legs));
    }
}