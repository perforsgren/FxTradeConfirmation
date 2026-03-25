using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using FxTradeConfirmation.Models;

namespace FxTradeConfirmation.Services;

public class RecentTradeService : IRecentTradeService
{
    private const int MaxEntries = 100;
    private const string IndexFileName = "recent_index.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _basePath;
    private readonly string _indexPath;

    public RecentTradeService(string basePath)
    {
        _basePath = basePath;
        _indexPath = Path.Combine(_basePath, IndexFileName);
    }

    public async Task SaveRecentTradeAsync(IReadOnlyList<TradeLeg> legs)
    {
        EnsureDirectory();

        var entry = BuildEntry(legs);
        var tradeData = new SavedTradeData { Legs = legs.ToList() };

        // Write trade file
        var tradePath = Path.Combine(_basePath, entry.FileName);
        var tradeJson = JsonSerializer.Serialize(tradeData, JsonOptions);
        await File.WriteAllTextAsync(tradePath, tradeJson);

        // Update index with retry for shared-file contention
        await WithFileRetryAsync(async () =>
        {
            var index = await ReadIndexCoreAsync();
            index.Entries.Insert(0, entry);

            // Enforce max entries — remove oldest beyond limit
            while (index.Entries.Count > MaxEntries)
            {
                var removed = index.Entries[^1];
                index.Entries.RemoveAt(index.Entries.Count - 1);

                // Best-effort delete of orphaned trade file
                try
                {
                    var orphanPath = Path.Combine(_basePath, removed.FileName);
                    if (File.Exists(orphanPath))
                        File.Delete(orphanPath);
                }
                catch { /* ignore */ }
            }

            var indexJson = JsonSerializer.Serialize(index, JsonOptions);
            await File.WriteAllTextAsync(_indexPath, indexJson);
        });
    }

    public async Task<IReadOnlyList<RecentTradeEntry>> LoadIndexAsync()
    {
        try
        {
            var index = await WithFileRetryAsync(ReadIndexCoreAsync);
            return index.Entries;
        }
        catch
        {
            return [];
        }
    }

    public async Task<SavedTradeData?> LoadTradeAsync(RecentTradeEntry entry)
    {
        var tradePath = Path.Combine(_basePath, entry.FileName);
        if (!File.Exists(tradePath))
            return null;

        var json = await File.ReadAllTextAsync(tradePath);
        return JsonSerializer.Deserialize<SavedTradeData>(json, JsonOptions);
    }

    public async Task DeleteTradeAsync(RecentTradeEntry entry)
    {
        // Remove from index
        await WithFileRetryAsync(async () =>
        {
            var index = await ReadIndexCoreAsync();
            index.Entries.RemoveAll(e => e.Id == entry.Id);

            var indexJson = JsonSerializer.Serialize(index, JsonOptions);
            await File.WriteAllTextAsync(_indexPath, indexJson);
        });

        // Delete the trade file
        try
        {
            var tradePath = Path.Combine(_basePath, entry.FileName);
            if (File.Exists(tradePath))
                File.Delete(tradePath);
        }
        catch { /* ignore — index is already updated */ }
    }

    private static RecentTradeEntry BuildEntry(IReadOnlyList<TradeLeg> legs)
    {
        var leg1 = legs.FirstOrDefault();
        var id = Guid.NewGuid().ToString("N");

        var legParts = legs.Select(l => $"{l.BuySell} {l.CallPut}");
        var legSummary = string.Join(" / ", legParts);

        return new RecentTradeEntry
        {
            Id = id,
            Username = Environment.UserName.ToUpperInvariant(),
            SavedDate = DateTime.UtcNow,
            Counterparty = leg1?.Counterpart ?? string.Empty,
            CurrencyPair = leg1?.CurrencyPair ?? string.Empty,
            LegCount = legs.Count,
            TradeDate = leg1?.ExpiryDate,
            FileName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Environment.UserName}_{id[..8]}.json",
            LegSummary = legSummary
        };
    }

    private async Task<RecentTradeIndex> ReadIndexCoreAsync()
    {
        if (!File.Exists(_indexPath))
            return new RecentTradeIndex();

        var json = await File.ReadAllTextAsync(_indexPath);
        return JsonSerializer.Deserialize<RecentTradeIndex>(json, JsonOptions) ?? new RecentTradeIndex();
    }

    private void EnsureDirectory()
    {
        if (!Directory.Exists(_basePath))
            Directory.CreateDirectory(_basePath);
    }

    /// <summary>
    /// Retries a file operation up to 3 times with short delays to handle
    /// concurrent access on shared network drives.
    /// </summary>
    private static async Task WithFileRetryAsync(Func<Task> action)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (IOException) when (attempt < 3)
            {
                await Task.Delay(200 * (attempt + 1));
            }
        }
    }

    private static async Task<T> WithFileRetryAsync<T>(Func<Task<T>> action)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (IOException) when (attempt < 3)
            {
                await Task.Delay(200 * (attempt + 1));
            }
        }
    }
}