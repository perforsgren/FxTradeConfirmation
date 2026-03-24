using FxTradeConfirmation.Models;

namespace FxTradeConfirmation.Services;

/// <summary>
/// Result of submitting a single trade payload to the STP hub.
/// </summary>
public record TradeSubmitResult(bool Success, long? MessageInId, string? ErrorMessage);

/// <summary>
/// Abstracts the FxTradeHub ExternalTradeIngestService so the ViewModel
/// does not depend directly on the STP hub assemblies.
/// </summary>
public interface ITradeIngestService
{
    /// <summary>
    /// Submits all legs (option + optional hedge per leg) to the STP hub.
    /// Returns one result per submitted payload.
    /// </summary>
    Task<IReadOnlyList<TradeSubmitResult>> SubmitTradeAsync(IReadOnlyList<TradeLeg> legs);
}