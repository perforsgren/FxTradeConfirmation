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
    private const string Tag = nameof(RecentTradeService);

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
        _basePath = Path.GetFullPath(basePath);
        _indexPath = Path.Combine(_basePath, IndexFileName);
        _lockPath = Path.Combine(_basePath, LockFileName);
    }

    public async Task SaveRecentTradeAsync(IReadOnlyList<TradeLeg> legs)
    {
        EnsureDirectory();

        var entry = BuildEntry(legs);
        var tradeData = new SavedTradeData { Legs = legs.ToList() };

        // Write the individual trade file first (no contention — unique file name)
        var tradePath = ResolveTradeFilePath(entry.FileName);
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
                    var orphanPath = ResolveTradeFilePath(removed.FileName);
                    if (File.Exists(orphanPath))
                        File.Delete(orphanPath);
                }
                catch (Exception ex)
                {
                    FileLogger.Instance?.Warn(Tag,
                        $"Could not delete pruned trade file '{removed.FileName}': {ex.GetType().Name}: {ex.Message}");
                }
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
        catch (Exception ex)
        {
            FileLogger.Instance?.Warn(Tag,
                $"Failed to load recent trade index, returning empty list: {ex.GetType().Name}: {ex.Message}");
            return [];
        }
    }

    public async Task<SavedTradeData?> LoadTradeAsync(RecentTradeEntry entry)
    {
        var tradePath = ResolveTradeFilePath(entry.FileName);
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
            var tradePath = ResolveTradeFilePath(entry.FileName);
            if (File.Exists(tradePath))
                File.Delete(tradePath);
        }
        catch (Exception ex)
        {
            FileLogger.Instance?.Warn(Tag,
                $"Could not delete trade file '{entry.FileName}' after index update: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ----------------------------------------------------------------
    //  H14: Path traversal guard
    //  All entry.FileName values are resolved through this method.
    //  It verifies that the final path stays within _basePath, blocking
    //  entries like "../../etc/passwd" from a corrupt index file.
    // ----------------------------------------------------------------

    /// <summary>
    /// Resolves <paramref name="fileName"/> to a full path under <see cref="_basePath"/>
    /// and throws <see cref="InvalidOperationException"/> if the result escapes the
    /// base directory (path traversal attempt).
    /// </summary>
    private string ResolveTradeFilePath(string fileName)
    {
        // Reject anything that looks like a rooted path or contains directory separators
        // before even calling Path.GetFullPath — avoids platform edge cases.
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException(
                $"Invalid trade file name: '{fileName}'.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(_basePath, fileName));

        // Ensure the resolved path is strictly inside _basePath.
        // Use OrdinalIgnoreCase to handle case-insensitive file systems (Windows/NTFS).
        if (!fullPath.StartsWith(_basePath + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Path traversal detected: '{fileName}' resolves outside the base directory.");
        }

        return fullPath;
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

        // H15: File.Move with overwrite:true is atomic on local NTFS but is NOT
        // guaranteed to be atomic on SMB/network shares — .NET makes no such promise.
        // Strategy: attempt Move first (fast path, atomic on local NTFS).
        // If Move fails (e.g. cross-volume or SMB rename rejection), fall back to
        // Delete+Move which is non-atomic but still leaves a consistent file:
        // the worst case is a missing index (rebuilt as empty) rather than corruption.
        var tmpPath = _indexPath + ".tmp." + Environment.ProcessId;
        try
        {
            await File.WriteAllTextAsync(tmpPath, json);

            try
            {
                File.Move(tmpPath, _indexPath, overwrite: true);
            }
            catch (IOException)
            {
                // Fallback for SMB shares where atomic rename is not guaranteed:
                // delete the target first, then move. Non-atomic but safe — a crash
                // here leaves no index rather than a corrupt one; the empty-index
                // fallback in ReadIndexCoreAsync handles that.
                try { File.Delete(_indexPath); } catch { /* ignore if already gone */ }
                File.Move(tmpPath, _indexPath, overwrite: false);
            }
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
    //  Index writes use a write-then-rename commit so the index is never left
    //  in a half-written state even if the process is killed mid-write.
    //  Note: rename atomicity is guaranteed on local NTFS but not on SMB.
    // ----------------------------------------------------------------

    private const int MaxAttempts = 15;
    private const int RetryDelayMs = 200;
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
            catch (IOException ex) when (attempt < MaxAttempts - 1)
            {
                // Lock held by another live process — wait and retry.
                FileLogger.Instance?.Warn(Tag,
                    $"Index lock contention (attempt {attempt + 1}/{MaxAttempts}): {ex.Message}");
                await Task.Delay(RetryDelayMs);
            }
            finally
            {
                lockFile?.Dispose();
            }
        }

        var finalEx = new IOException(
            "Could not acquire index lock after multiple attempts. " +
            "Another process may be holding the lock file on the network share.");
        FileLogger.Instance?.Error(Tag, finalEx.Message);
        throw finalEx;
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
        catch (Exception ex)
        {
            FileLogger.Instance?.Warn(Tag,
                $"Could not break stale lock file: {ex.GetType().Name}: {ex.Message}");
        }
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
