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
public sealed class OptionQueryFilter : IOptionQueryFilter
{
    private readonly string _filePath;
    private IReadOnlyList<KeywordRule> _rules = [];

    public OptionQueryFilter(string filePath)
    {
        _filePath = filePath;
        Reload();
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

    // ── JSON deserialization models ──────────────────────────────────────────

    private sealed record KeywordsFile(List<KeywordEntry> Keywords);

    private sealed record KeywordEntry(string Word, string? MatchType);

    private sealed record KeywordRule(string Word, string MatchType)
    {
        public string Pattern { get; } = $@"\b{Regex.Escape(Word)}\b";
    }
}