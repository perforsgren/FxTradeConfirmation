
namespace FxTradeConfirmation.Models;

/// <summary>
/// Snapshot of the current workspace persisted as a local draft file.
/// </summary>
public record DraftData(
    DateTime SavedAt,
    string Counterpart,
    List<TradeLeg> Legs);
