using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using FxTradeConfirmation.Models;

namespace FxTradeConfirmation.Services;

public class RecentTradeService : IRecentTradeService
{
    private const int MaxEntries = 100;
    private const string IndexFileName = "recent_index.json";
    private const string LockFileName = "recent_index.lock";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _basePath;
    private readonly string _indexPath;
    private readonly string _lockPath;

    public RecentTradeService(string basePath)
    {
        _basePath = basePath;
        _indexPath = Path.Combine(_basePath, IndexFileName);
        _lockPath = Path.Combine(_basePath, LockFileName);
    }

    public async Task SaveRecentTradeAsync(IReadOnlyList<TradeLeg> legs)
    {
        EnsureDirectory();

        var entry = BuildEntry(legs);
        var tradeData = new SavedTradeData { Legs = legs.ToList() };

        // Write the individual trade file first (no contention — unique file name)
        var tradePath = Path.Combine(_basePath, entry.FileName);
        var tradeJson = JsonSerializer.Serialize(tradeData, JsonOptions);
        await File.WriteAllTextAsync(tradePath, tradeJson);

        // Update the shared index under an exclusive lock
        await WithIndexLockAsync(async () =>
        {
            var index = await ReadIndexCoreAsync();
            index.Entries.Insert(0, entry);

            // Enforce max entries — prune oldest beyond the cap
            while (index.Entries.Count > MaxEntries)
            {
                var removed = index.Entries[^1];
                index.Entries.RemoveAt(index.Entries.Count - 1);

                try
                {
                    var orphanPath = Path.Combine(_basePath, removed.FileName);
                    if (File.Exists(orphanPath))
                        File.Delete(orphanPath);
                }
                catch { /* best-effort */ }
            }

            await WriteIndexCoreAsync(index);
        });
    }

    public async Task<IReadOnlyList<RecentTradeEntry>> LoadIndexAsync()
    {
        try
        {
            // Read-only: no lock needed — we tolerate a stale snapshot here
            var index = await ReadIndexCoreAsync();
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
        await WithIndexLockAsync(async () =>
        {
            var index = await ReadIndexCoreAsync();
            index.Entries.RemoveAll(e => e.Id == entry.Id);
            await WriteIndexCoreAsync(index);
        });

        // Delete the trade file outside the lock — unique name, no contention
        try
        {
            var tradePath = Path.Combine(_basePath, entry.FileName);
            if (File.Exists(tradePath))
                File.Delete(tradePath);
        }
        catch { /* index already updated — ignore stale file */ }
    }

    // ----------------------------------------------------------------
    //  Index helpers
    // ----------------------------------------------------------------

    private async Task<RecentTradeIndex> ReadIndexCoreAsync()
    {
        if (!File.Exists(_indexPath))
            return new RecentTradeIndex();

        var json = await File.ReadAllTextAsync(_indexPath);
        return JsonSerializer.Deserialize<RecentTradeIndex>(json, JsonOptions)
               ?? new RecentTradeIndex();
    }

    private async Task WriteIndexCoreAsync(RecentTradeIndex index)
    {
        var json = JsonSerializer.Serialize(index, JsonOptions);

        // Atomic commit: write to a temp file then rename over the real index.
        // File.Move with overwrite:true is atomic on NTFS/SMB — the index is
        // never left in a half-written state if the process is killed mid-write.
        var tmpPath = _indexPath + ".tmp." + Environment.ProcessId;
        try
        {
            await File.WriteAllTextAsync(tmpPath, json);
            File.Move(tmpPath, _indexPath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmpPath); } catch { /* ignore cleanup failure */ }
            throw;
        }
    }

    private void EnsureDirectory()
    {
        if (!Directory.Exists(_basePath))
            Directory.CreateDirectory(_basePath);
    }

    // ----------------------------------------------------------------
    //  Exclusive file lock — prevents lost-update race conditions when
    //  multiple users save simultaneously on the shared network drive.
    //
    //  Strategy: open (or create) a dedicated .lock file with FileShare.None.
    //  If the lock file is older than StaleLockAgeSeconds it is considered a
    //  stale lock left by a crashed process and is deleted before retrying.
    //  Any competing writer backs off up to MaxAttempts × RetryDelayMs before
    //  giving up.
    //
    //  Index writes use an atomic write-then-rename commit so the index is
    //  never left in a half-written state even if the process is killed
    //  between the write and the rename.
    // ----------------------------------------------------------------

    private const int MaxAttempts       = 15;
    private const int RetryDelayMs      = 200;
    private const int StaleLockAgeSeconds = 30;

    private async Task WithIndexLockAsync(Func<Task> action)
    {
        EnsureDirectory();

        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            // Break a stale lock left by a crashed process.
            TryBreakStaleLock();

            FileStream? lockFile = null;
            try
            {
                lockFile = new FileStream(
                    _lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);

                await action();
                return;
            }
            catch (IOException) when (attempt < MaxAttempts - 1)
            {
                // Lock held by another live process — wait and retry.
                await Task.Delay(RetryDelayMs);
            }
            finally
            {
                lockFile?.Dispose();
            }
        }

        throw new IOException(
            "Could not acquire index lock after multiple attempts. " +
            "Another process may be holding the lock file on the network share.");
    }

    /// <summary>
    /// Deletes the lock file if it is older than <see cref="StaleLockAgeSeconds"/>,
    /// indicating it was left behind by a process that crashed while holding the lock.
    /// </summary>
    private void TryBreakStaleLock()
    {
        try
        {
            if (!File.Exists(_lockPath))
                return;

            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(_lockPath);
            if (age.TotalSeconds > StaleLockAgeSeconds)
                File.Delete(_lockPath);
        }
        catch { /* best-effort — another process may have already removed it */ }
    }

    // ----------------------------------------------------------------
    //  Entry builder
    // ----------------------------------------------------------------

    private static RecentTradeEntry BuildEntry(IReadOnlyList<TradeLeg> legs)
    {
        var leg1 = legs.FirstOrDefault();
        var id = Guid.NewGuid().ToString("N");

        var legSummary = string.Join(" / ", legs.Select(l => $"{l.BuySell} {l.CallPut}"));

        return new RecentTradeEntry
        {
            Id = id,
            Username = Environment.UserName.ToUpperInvariant(),
            SavedDate = DateTime.UtcNow,
            Counterparty = leg1?.Counterpart ?? string.Empty,
            CurrencyPair = leg1?.CurrencyPair ?? string.Empty,
            LegCount = legs.Count,
            ExpiryDate = leg1?.ExpiryDate,
            FileName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Environment.UserName}_{id[..8]}.json",
            LegSummary = legSummary
        };
    }
}