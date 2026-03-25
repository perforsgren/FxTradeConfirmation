using System.Text.Json.Serialization;

namespace FxTradeConfirmation.Models;

/// <summary>
/// Metadata for a single saved trade, shown in the "Open Recent" dialog.
/// Stored in the shared index file.
/// </summary>
public class RecentTradeEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Username { get; set; } = string.Empty;
    public DateTime SavedDate { get; set; } = DateTime.UtcNow;
    public string Counterparty { get; set; } = string.Empty;
    public string CurrencyPair { get; set; } = string.Empty;
    public int LegCount { get; set; }

    /// <summary>Expiry date of leg 1. Previously mis-named TradeDate in the JSON.</summary>
    public DateTime? ExpiryDate { get; set; }

    /// <summary>Backward-compat alias: old index files used "TradeDate" for this field.</summary>
    [JsonPropertyName("TradeDate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? TradeDateLegacy
    {
        get => null;                  // never write the old name
        set => ExpiryDate ??= value;  // on read: populate ExpiryDate if not already set
    }

    public string FileName { get; set; } = string.Empty;

    /// <summary>Summary string for quick display: "Buy Call / Sell Put" etc.</summary>
    public string LegSummary { get; set; } = string.Empty;
}

/// <summary>
/// Root object for the shared index JSON file.
/// </summary>
public class RecentTradeIndex
{
    public List<RecentTradeEntry> Entries { get; set; } = [];
}

/// <summary>
/// Full trade data stored per trade file.
/// </summary>
public class SavedTradeData
{
    public List<TradeLeg> Legs { get; set; } = [];
}