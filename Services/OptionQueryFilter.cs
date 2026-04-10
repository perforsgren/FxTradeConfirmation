using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FxTradeConfirmation.Services;

/// <summary>
/// Loads keyword rules from a JSON file and matches clipboard text against them.
/// Supports two match types:
///   "wholeword"  – \bword\b regex, avoids false hits like "exp" in "expensive"
///   "substring"  – simple case-insensitive Contains
/// </summary>
/// <remarks>
/// A <see cref="FileSystemWatcher"/> monitors the keywords file for changes and
/// automatically reloads rules without requiring an application restart.
/// Multiple rapid change events (common with text editors) are debounced via a
/// <see cref="CancellationTokenSource"/> so that only one reload fires per save.
/// </remarks>
public sealed class OptionQueryFilter : IOptionQueryFilter, IDisposable
{
    private readonly string _filePath;

    // volatile ensures that writes from the thread-pool reload are immediately
    // visible to reads on the UI thread without a memory barrier or lock.
    private volatile IReadOnlyList<KeywordRule> _rules = [];

    // Watches the keywords file for external edits (e.g. a network admin updating
    // Keywords.json on the share). Disposed deterministically in Dispose().
    private readonly FileSystemWatcher? _watcher;

    // Debounce state — cancels any pending reload when a new change event arrives.
    private CancellationTokenSource? _debounceCts;

    public OptionQueryFilter(string filePath)
    {
        _filePath = filePath;
        Reload();

        // Set up a watcher only if the file's directory exists and is reachable.
        // If the network share is unavailable at startup the watcher is skipped
        // gracefully — Reload() already defaulted _rules to [].
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            var file = Path.GetFileName(filePath);

            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                _watcher = new FileSystemWatcher(dir, file)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };

                // Changed fires multiple times during a single save on some editors;
                // both Changed and Created cover in-place saves and replace-writes.
                _watcher.Changed += OnFileChanged;
                _watcher.Created += OnFileChanged;
            }
        }
        catch
        {
            // Watcher setup is best-effort — a missing or inaccessible directory
            // is not fatal. The filter will continue working with the rules loaded
            // at construction time.
            _watcher = null;
        }
    }

    public bool IsOptionQuery(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (var rule in _rules)
        {
            bool matched = rule.MatchType == "wholeword"
                ? Regex.IsMatch(text, rule.Pattern, RegexOptions.IgnoreCase)
                : text.Contains(rule.Word, StringComparison.OrdinalIgnoreCase);

            if (matched) return true;
        }

        return false;
    }

    public void Reload()
    {
        if (!File.Exists(_filePath))
        {
            _rules = [];
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var doc = JsonSerializer.Deserialize<KeywordsFile>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            _rules = doc?.Keywords
                .Where(k => !string.IsNullOrWhiteSpace(k.Word))
                .Select(k => new KeywordRule(k.Word.Trim(), k.MatchType ?? "substring"))
                .ToList()
                ?? [];
        }
        catch
        {
            _rules = [];
        }
    }

    public void Dispose()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();

        if (_watcher is null) return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnFileChanged;
        _watcher.Created -= OnFileChanged;
        _watcher.Dispose();
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Cancel any previously scheduled reload — editors often fire 2–3 Changed
        // events per save. Only the last one (after the 300 ms quiet period) runs.
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;

        Task.Delay(300, cts.Token).ContinueWith(
            _ => Reload(),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }

    // ── JSON deserialization models ──────────────────────────────────────────

    private sealed record KeywordsFile(List<KeywordEntry> Keywords);

    private sealed record KeywordEntry(string Word, string? MatchType);

    private sealed record KeywordRule(string Word, string MatchType)
    {
        public string Pattern { get; } = $@"\b{Regex.Escape(Word)}\b";
    }
}
