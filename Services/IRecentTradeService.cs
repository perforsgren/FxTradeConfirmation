using FxTradeConfirmation.Models;

namespace FxTradeConfirmation.Services;

/// <summary>
/// Manages saving and loading recent trades from a shared network location.
/// </summary>
public interface IRecentTradeService
{
    /// <summary>
    /// Saves a trade to the shared location and adds an entry to the index.
    /// </summary>
    Task SaveRecentTradeAsync(IReadOnlyList<TradeLeg> legs);

    /// <summary>
    /// Loads all entries from the index file.
    /// </summary>
    Task<IReadOnlyList<RecentTradeEntry>> LoadIndexAsync();

    /// <summary>
    /// Loads the full trade data for a specific entry.
    /// </summary>
    Task<SavedTradeData?> LoadTradeAsync(RecentTradeEntry entry);

    /// <summary>
    /// Removes an entry from the index and deletes its trade file from disk.
    /// </summary>
    Task DeleteTradeAsync(RecentTradeEntry entry);
}