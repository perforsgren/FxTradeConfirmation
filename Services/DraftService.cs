using FxTradeConfirmation.Models;
using System.Diagnostics;
using System.Text.Json;
using System.IO;

namespace FxTradeConfirmation.Services;

/// <summary>
/// Persists and restores a workspace draft to a local JSON file.
/// Thread-safe: concurrent callers share a single <see cref="SemaphoreSlim"/>.
/// </summary>
public class DraftService : IDisposable
{
    private static readonly string DraftFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FxTradeConfirmation",
        "draft.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// True while a save operation is in progress.
    /// Use this to skip a new auto-save tick rather than queuing behind a stalled write.
    /// </summary>
    public bool IsSaving => _lock.CurrentCount == 0;

    /// <summary>
    /// Serializes <paramref name="legs"/> and <paramref name="counterpart"/> to the draft file.
    /// Safe to call concurrently — extra callers wait rather than corrupt the file.
    /// </summary>
    public async Task SaveDraftAsync(IEnumerable<TradeLeg> legs, string counterpart)
    {
        var draft = new DraftData(
            SavedAt: DateTime.Now,
            Counterpart: counterpart,
            Legs: legs.ToList());

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var dir = Path.GetDirectoryName(DraftFilePath)!;
            Directory.CreateDirectory(dir);

            await using var fs = new FileStream(
                DraftFilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true);

            await JsonSerializer.SerializeAsync(fs, draft, JsonOptions).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DraftService] SaveDraftAsync failed: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Tries to load and deserialize the draft file.
    /// Returns <c>false</c> — and sets <paramref name="draft"/> to <c>null</c> — if the
    /// file does not exist or its contents are corrupt/unreadable.
    /// </summary>
    public bool TryLoadDraft(out DraftData? draft)
    {
        draft = null;

        if (!File.Exists(DraftFilePath))
            return false;

        try
        {
            using var fs = new FileStream(
                DraftFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            draft = JsonSerializer.Deserialize<DraftData>(fs, JsonOptions);
            return draft is not null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DraftService] TryLoadDraft failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Deletes the draft file. Safe to call when no file exists.
    /// </summary>
    public void DeleteDraft()
    {
        try
        {
            if (File.Exists(DraftFilePath))
                File.Delete(DraftFilePath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DraftService] DeleteDraft failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}
