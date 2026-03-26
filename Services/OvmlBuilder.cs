using FxTradeConfirmation.Models;
using OpenAI.Chat;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FxTradeConfirmation.Services;

/// <summary>
/// AI-based OVML parser using OpenAI gpt-4o as fallback when the regex parser fails.
/// Runs up to two passes: pass 2 is triggered when spot or put/call is missing.
/// </summary>
public sealed class OvmlBuilder : IOvmlParser
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly HashSet<string> IsoCcy = new(StringComparer.OrdinalIgnoreCase)
    {
        "USD","EUR","SEK","NOK","DKK","GBP","CHF","JPY","CAD","AUD","NZD",
        "CZK","PLN","HUF","TRY","ZAR","MXN","BRL","CNY","CNH","HKD","SGD","KRW","INR"
    };

    // ── Config ───────────────────────────────────────────────────────────────
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _promptFilePath;

    // ── Debug / diagnostics ──────────────────────────────────────────────────
    public string LastJson { get; private set; } = string.Empty;
    public string LastPair { get; private set; } = string.Empty;
    public string LastSpot { get; private set; } = string.Empty;
    public int LastPassesUsed { get; private set; }
    public bool LastWasCanceled { get; private set; }

    public OvmlBuilder(string promptFilePath, string? apiKey = null, string model = "gpt-5.4-mini")   //gpt-4o
    {
        _promptFilePath = promptFilePath;
        _model = string.IsNullOrWhiteSpace(model) ? "gpt-5.4-mini" : model;  //gpt-4o
        _apiKey = ResolveApiKey(apiKey, promptFilePath);
    }

    // ── IOvmlParser (sync — safe bridge via Task.Run) ────────────────────────

    public bool TryParse(string input, out string ovml, out IReadOnlyList<OvmlLeg> legs)
    {
        // Task.Run strips the SynchronizationContext, so the inner await can never deadlock.
        var result = Task.Run(() => TryParseAsync(input, CancellationToken.None)).GetAwaiter().GetResult();
        ovml = result.Ovml;
        legs = result.Legs;
        return result.Success;
    }

    // ── IOvmlParser (async — preferred entry point) ──────────────────────────

    public async Task<(bool Success, string Ovml, IReadOnlyList<OvmlLeg> Legs)> TryParseAsync(
        string input, CancellationToken ct = default)
    {
        try
        {
            var prompt = File.Exists(_promptFilePath)
                ? await File.ReadAllTextAsync(_promptFilePath, ct)
                : string.Empty;
            var result = await GenerateAsync(input, prompt, ct);
            var success = !string.IsNullOrEmpty(result.Ovml) && result.Legs.Count > 0;
            return (success, result.Ovml, result.Legs);
        }
        catch
        {
            return (false, string.Empty, []);
        }
    }

    // ── Core generation (sync bridge) ────────────────────────────────────────

    public ParseResult Generate(string input, string systemPrompt, CancellationToken ct)
    {
        return Task.Run(() => GenerateAsync(input, systemPrompt, ct), ct).GetAwaiter().GetResult();
    }

    // ── Core generation (async) ──────────────────────────────────────────────

    public async Task<ParseResult> GenerateAsync(string input, string systemPrompt, CancellationToken ct)
    {
        LastJson = string.Empty;
        LastPair = string.Empty;
        LastSpot = string.Empty;
        LastPassesUsed = 0;
        LastWasCanceled = false;

        if (string.IsNullOrWhiteSpace(input))
            return ParseResult.Empty;

        var client = new ChatClient(_model, _apiKey);
        ct.ThrowIfCancellationRequested();

        // ── Pass 1 ───────────────────────────────────────────────────────────
        var raw1 = await CallApiAsync(client, input, systemPrompt, ct);
        if (LastWasCanceled) return ParseResult.Empty;

        var p1 = TryParseJson(raw1);
        if (p1 is null) { LastPassesUsed = 1; return ParseResult.Empty; }

        NormalizePair(p1);
        ApplyAtmFromSpot(p1, input);
        InferPutCall(p1);

        LastPassesUsed = 1;
        Snapshot(p1);

        if (!NeedsSecondPass(p1))
            return Finish(p1);

        ct.ThrowIfCancellationRequested();

        // ── Pass 2 ───────────────────────────────────────────────────────────
        var extSpot = TryExtractSpotFromText(input)
           ?? await TryFetchSpotAsync(Safe(p1.Pair), ct);
        var modified = input;
        if (extSpot.HasValue)
            modified += " sp ref " + extSpot.Value.ToString(CultureInfo.InvariantCulture);

        var raw2 = await CallApiAsync(client, modified, systemPrompt, ct);
        if (LastWasCanceled) return Finish(p1); // return pass-1 result on cancel

        var p2 = TryParseJson(raw2);
        if (p2 is null) { LastPassesUsed = 2; return Finish(p1); }

        NormalizePair(p2);
        ApplyAtmFromSpot(p2, input);
        InferPutCall(p2);

        LastPassesUsed = 2;
        Snapshot(p2);

        return Finish(p2);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<string> CallApiAsync(ChatClient client, string userInput, string systemPrompt, CancellationToken ct)
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var sysText = (systemPrompt ?? string.Empty).Replace("{{TODAY}}", today);
        var userText = $"(Idag är {today}.) {userInput}";

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(sysText),
            new UserChatMessage(userText)
        };

        try
        {
            var result = await client.CompleteChatAsync(messages, cancellationToken: ct);
            return result.Value.Content.Count > 0
                ? result.Value.Content[0].Text
                : string.Empty;
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            LastWasCanceled = true;
            return string.Empty;
        }
    }

    private ParseResult Finish(ParsedOutput po)
    {
        var ovml = ToOvml(po);
        var legs = BuildLegs(po);
        return new ParseResult(ovml, legs);
    }

    private void Snapshot(ParsedOutput po)
    {
        LastJson = JsonSerializer.Serialize(po, JsonOpts);
        LastPair = Safe(po.Pair);
        LastSpot = Safe(po.Spot);
    }

    private static void NormalizePair(ParsedOutput po)
    {
        if (!IsValidPair(po.Pair)) po.Pair = "UNKNOWN";
    }

    private static bool NeedsSecondPass(ParsedOutput po)
    {
        if (string.IsNullOrWhiteSpace(Safe(po.Spot))) return true;
        return po.Legs?.Any(l => l != null && string.IsNullOrWhiteSpace(Safe(l.PutCall))) ?? false;
    }

    private static void InferPutCall(ParsedOutput po)
    {
        if (po.Legs is null || string.IsNullOrWhiteSpace(Safe(po.Spot))) return;
        if (!decimal.TryParse(Safe(po.Spot), NumberStyles.Any, CultureInfo.InvariantCulture, out var spot)) return;

        var tol = Math.Abs(spot) * 0.0005m;

        foreach (var leg in po.Legs)
        {
            if (leg is null || !string.IsNullOrWhiteSpace(Safe(leg.PutCall))) continue;
            if (!decimal.TryParse(Safe(leg.Strike).Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var k)) continue;

            var diff = k - spot;
            if (Math.Abs(diff) <= tol) { leg.Strike = "ATM"; leg.PutCall = string.Empty; }
            else leg.PutCall = diff > 0 ? "Call" : "Put";
        }
    }

    private static void ApplyAtmFromSpot(ParsedOutput po, string originalInput)
    {
        if (po.Legs is null) return;
        if (originalInput.IndexOf("ATM", StringComparison.OrdinalIgnoreCase) < 0) return;
        if (!decimal.TryParse(Safe(po.Spot).Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var spot)) return;

        var spotStr = spot.ToString(CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');

        foreach (var leg in po.Legs)
        {
            if (leg is null) continue;
            if (string.Equals(Safe(leg.Strike), "ATM", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(Safe(leg.Strike)))
                leg.Strike = spotStr;
            if (string.IsNullOrWhiteSpace(Safe(leg.PutCall)))
                leg.PutCall = "Call";
        }
    }

    private static string ToOvml(ParsedOutput po)
    {
        if (po?.Legs is null) return string.Empty;

        InferPutCall(po);

        var pair = string.IsNullOrWhiteSpace(Safe(po.Pair)) ? "UNKNOWN" : Safe(po.Pair).ToUpperInvariant();
        var priceCcy = pair.Length == 6 ? pair[3..] : string.Empty;

        var bs = po.Legs.Select(l => Safe(l?.BuySell).ToUpperInvariant() == "BUY" ? "B" : "S").ToList();

        var cps = po.Legs.Select(l =>
        {
            var pc = Safe(l?.PutCall).ToUpperInvariant();
            var prefix = pc == "CALL" ? "C" : pc == "PUT" ? "P" : string.Empty;
            var strike = NormalizeStrike(Safe(l?.Strike));
            return string.IsNullOrEmpty(prefix) ? strike : prefix + strike;
        }).ToList();

        var ns = po.Legs
            .Select(l => Safe(l?.Notional).ToUpperInvariant())
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();

        var expiry = Safe(po.Expiry);
        if (string.IsNullOrWhiteSpace(expiry) && po.Expiries?.Count > 0)
            expiry = string.Join(",", po.Expiries.Select(Safe));

        var notionalPart = ns.Count == 0 ? null
            : ns.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
                ? "N" + ns[0]
                : "N" + string.Join(",", ns);

        var parts = new List<string> { "OVML", pair, string.Join(",", bs), string.Join(",", cps) };
        if (!string.IsNullOrEmpty(notionalPart)) parts.Add(notionalPart);
        if (!string.IsNullOrEmpty(expiry)) parts.Add(expiry);
        if (!string.IsNullOrEmpty(priceCcy)) parts.Add("PC" + priceCcy);
        if (!string.IsNullOrEmpty(Safe(po.Spot))) parts.Add("SP" + Safe(po.Spot));

        return string.Join(" ", parts);
    }

    private static List<OvmlLeg> BuildLegs(ParsedOutput po)
    {
        if (po?.Legs is null) return [];

        var pair = Safe(po.Pair).ToUpperInvariant();
        var spot = Safe(po.Spot);
        var sharedExpiry = Safe(po.Expiry);

        return po.Legs
            .Select((l, i) =>
            {
                var expiry = !string.IsNullOrWhiteSpace(sharedExpiry) ? sharedExpiry
                    : po.Expiries is not null && i < po.Expiries.Count ? Safe(po.Expiries[i])
                    : string.Empty;

                return new OvmlLeg(
                    Pair: pair,
                    BuySell: Safe(l?.BuySell),
                    PutCall: Safe(l?.PutCall),
                    Strike: NormalizeStrike(Safe(l?.Strike)),
                    Notional: ParseNotional(Safe(l?.Notional)),
                    Expiry: expiry,
                    Spot: spot);
            })
            .ToList();
    }

    // ── Static utilities ─────────────────────────────────────────────────────

    private static bool IsValidPair(string? pair)
    {
        if (string.IsNullOrWhiteSpace(pair) || pair.Trim().Length != 6) return false;
        var p = pair.Trim().ToUpperInvariant();
        return p[..3] != p[3..] && IsoCcy.Contains(p[..3]) && IsoCcy.Contains(p[3..]);
    }

    private static decimal? TryExtractSpotFromText(string text)
    {
        var m = Regex.Match(text, @"(?ix)\b(?:sp(?:ot)?(?:\s*ref)?|ref|sr)\s*[:=]?\s*([+-]?(?:\d{1,3}(?:[.,]\d{3})*|\d+)(?:[.,]\d+)?)\b");
        if (!m.Success) return null;

        var norm = NormalizeFxNumber(m.Groups[1].Value);
        return decimal.TryParse(norm, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static async Task<decimal?> TryFetchSpotAsync(string pair, CancellationToken ct)
    {
        if (!IsValidPair(pair))
            return null;

        // Hard ceiling: never let BLPAPI block the parse flow for more than 6 seconds
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(6));

        try
        {
            return await BloombergFx.GetFxSpotMidAsync(pair, ct: cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // BLPAPI timed out but the caller didn't cancel — treat as "no spot"
            return null;
        }
    }

    private static string NormalizeFxNumber(string raw)
    {
        raw = raw.Replace(" ", string.Empty);
        int commas = raw.Count(c => c == ','), dots = raw.Count(c => c == '.');

        if (commas > 0 && dots > 0)
            return raw.LastIndexOf('.') > raw.LastIndexOf(',')
                ? raw.Replace(",", string.Empty)
                : raw.Replace(".", string.Empty).Replace(",", ".");

        if (commas == 1)
        {
            var p = raw.Split(',');
            return p[1].Length == 3 ? p[0] + p[1] : p[0] + "." + p[1];
        }

        if (dots > 1)
        {
            int last = raw.LastIndexOf('.');
            return raw[..last].Replace(".", string.Empty) + "." + raw[(last + 1)..];
        }

        return raw;
    }

    private static string NormalizeStrike(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        if (s.Trim().Equals("ATM", StringComparison.OrdinalIgnoreCase)) return "ATM";
        if (!decimal.TryParse(s.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return s;
        var t = d.ToString(CultureInfo.InvariantCulture);
        return t.Contains('.') ? t.TrimEnd('0').TrimEnd('.') : t;
    }

    private static long ParseNotional(string n)
    {
        n = n.ToUpperInvariant().Replace(" ", string.Empty).Replace(",", string.Empty);
        if (n.EndsWith('M'))
            return decimal.TryParse(n[..^1], NumberStyles.Any, CultureInfo.InvariantCulture, out var m)
                ? (long)(m * 1_000_000m) : 0;
        return decimal.TryParse(n, NumberStyles.Any, CultureInfo.InvariantCulture, out var p)
            ? (long)(p * 1_000_000m) : 0;
    }

    private static string ResolveApiKey(string? ctorKey, string promptFilePath)
    {
        if (!string.IsNullOrWhiteSpace(ctorKey)) return ctorKey.Trim();

        var env = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim();

        try
        {
            var dir = Path.GetDirectoryName(promptFilePath);
            var keyPath = dir is not null ? Path.Combine(dir, "Key.txt") : null;
            if (keyPath is not null && File.Exists(keyPath))
                return File.ReadAllText(keyPath).Trim();
        }
        catch { }

        throw new InvalidOperationException(
            "OpenAI API key not found. Set OPENAI_API_KEY environment variable or place Key.txt beside Prompt.txt.");
    }

    private static string Safe(string? s) => s?.Trim() ?? string.Empty;

    // ── JSON DTOs ────────────────────────────────────────────────────────────

    private static ParsedOutput? TryParseJson(string text)
    {
        try
        {
            var t = text.Trim();
            if (t.StartsWith("```")) { var nl = t.IndexOf('\n'); if (nl >= 0) t = t[(nl + 1)..].Trim(); var f = t.LastIndexOf("```"); if (f >= 0) t = t[..f].Trim(); }
            var s = t.IndexOf('{'); var e = t.LastIndexOf('}');
            if (s < 0 || e <= s) return null;
            return JsonSerializer.Deserialize<ParsedOutput>(t[s..(e + 1)], JsonOpts);
        }
        catch { return null; }
    }

    private sealed class ParsedLeg
    {
        [JsonPropertyName("buySell")] public string? BuySell { get; set; }
        [JsonPropertyName("putCall")] public string? PutCall { get; set; }
        [JsonPropertyName("strike")] public string? Strike { get; set; }
        [JsonPropertyName("notional")] public string? Notional { get; set; }
    }

    private sealed class ParsedOutput
    {
        [JsonPropertyName("pair")] public string? Pair { get; set; }
        [JsonPropertyName("expiry")] public string? Expiry { get; set; }
        [JsonPropertyName("expiries")] public List<string>? Expiries { get; set; }
        [JsonPropertyName("spot")] public string? Spot { get; set; }
        [JsonPropertyName("legs")] public List<ParsedLeg>? Legs { get; set; }
        [JsonPropertyName("notionalInPriceCurrency")] public bool NotionalInPriceCurrency { get; set; }
    }
}

/// <summary>Result from a successful parse attempt.</summary>
public sealed record ParseResult(string Ovml, IReadOnlyList<OvmlLeg> Legs)
{
    public static readonly ParseResult Empty = new(string.Empty, []);
}