using System.Globalization;
using System.Text.RegularExpressions;
using FxTradeConfirmation.Models;

namespace FxTradeConfirmation.Services;

/// <summary>
/// Fast regex-based OVML parser (AP3). Attempts to extract pair, spot, expiry
/// and per-leg buy/sell, call/put, strike and notional from a free-text input.
/// Returns false if any mandatory field is missing — caller should fall back to AI.
/// </summary>
public sealed class OvmlBuilderAP3 : IOvmlParser
{
    // ── Regex constants ─────────────────────────────────────────────────────
    private static readonly Regex RxPairSpaced = new(@"\b([A-Za-z]{3})\s+([A-Za-z]{3})\b", RegexOptions.Compiled);
    private static readonly Regex RxPairCompact = new(@"\b([A-Za-z]{6})\b", RegexOptions.Compiled);
    private static readonly Regex RxPairSlash = new(@"\b([A-Za-z]{3})\/([A-Za-z]{3})\b", RegexOptions.Compiled);
    private static readonly Regex RxSpot = new(@"(?ix)\b(?:sp(?:ot)?(?:\s*ref)?|ref|sr)\s*[:=]?\s*([+-]?(?:\d{1,3}(?:[.,]\d{3})*|\d+)(?:[.,]\d+)?)\b", RegexOptions.Compiled);
    private static readonly Regex RxExpiry = new(@"expiry\s+(\d{1,2})\s*([A-Za-z]{3,})\s*(\d{4})?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxTenor = new(@"\b(\d{1,2}[MWDmwd])\b", RegexOptions.Compiled);
    private static readonly Regex RxLeg = new(
        @"\b(buy|köpa|sell|sälja)\b\s*(?:a)?\s*([0-9]*\.?[0-9]+[dD]?)?\s*(call|put)?\s*(?:in\s*([0-9,\.]*\s*(m|mio|milj|M)?))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxDeltaStrike = new(@"^(\d+(?:\.\d+)?)[dD]$", RegexOptions.Compiled);

    /// <summary>
    /// Matches the Bloomberg chat sender header: a line of ALL-CAPS words
    /// (e.g. "MATZ ERIKSSON") that appears before the first timestamp line.
    /// The name must consist solely of uppercase letters and spaces, be at
    /// least two words, and occupy its own line.
    /// </summary>
    private static readonly Regex RxSenderName = new(
        @"^\s*([A-ZÅÄÖÆØÜ][A-ZÅÄÖÆØÜ]+(?:\s+[A-ZÅÄÖÆØÜ][A-ZÅÄÖÆØÜ]+)+)\s*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// Known currency pairs loaded from reference data (e.g. "EURNOK", "USDSEK").
    /// Used as an allowlist so we never misidentify names or other words as a pair.
    /// </summary>
    private readonly HashSet<string> _knownPairs;

    public OvmlBuilderAP3(IEnumerable<string>? knownPairs = null)
    {
        _knownPairs = knownPairs is null
            ? []
            : new HashSet<string>(knownPairs.Select(p => p.ToUpperInvariant()), StringComparer.OrdinalIgnoreCase);
    }

    public bool TryParse(string input, out string ovml, out IReadOnlyList<OvmlLeg> legs)
    {
        ovml = string.Empty;
        legs = [];

        if (string.IsNullOrWhiteSpace(input))
            return false;

        var senderName = ExtractSenderName(input);
        var cleaned = CleanInput(input);
        var pair = ExtractPair(cleaned);
        var spot = ExtractSpot(cleaned);
        var expiry = ExtractExpiryOrTenor(cleaned);
        var legList = ExtractLegs(cleaned, pair, expiry, spot);

        if (!IsFullyPopulated(pair, expiry, spot, legList))
            return false;

        // Stamp the sender name on every leg so the caller can resolve Sales
        if (!string.IsNullOrEmpty(senderName))
            legList = legList.Select(l => l with { SenderName = senderName }).ToList();

        ovml = BuildOvml(pair, expiry, spot, legList);
        legs = legList;
        return !string.IsNullOrEmpty(ovml);
    }

    /// <summary>
    /// Rebuilds an OVML string from a list of <see cref="OvmlLeg"/> objects.
    /// Used to regenerate the OVML after the user toggles Buy/Sell or Call/Put in the dialog.
    /// </summary>
    public static string RebuildOvml(IReadOnlyList<OvmlLeg> legs)
    {
        if (legs is not { Count: > 0 })
            return string.Empty;

        var first = legs[0];
        var pair = string.IsNullOrWhiteSpace(first.Pair) ? "UNKNOWN" : first.Pair.ToUpperInvariant();
        var spot = first.Spot ?? string.Empty;
        var expiry = first.Expiry ?? string.Empty;

        return BuildOvml(pair, expiry, spot, legs.ToList());
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the Bloomberg chat sender name from the raw clipboard text.
    /// Bloomberg pastes typically start with the sender's full name in ALL CAPS
    /// on its own line before the first timestamp, e.g.:
    /// <code>
    /// MATZ ERIKSSON
    /// 15:04:24 eursek expiry …
    /// </code>
    /// Returns the matched name as pasted (upper-case), or an empty string when
    /// the header is absent.
    /// </summary>
    private static string ExtractSenderName(string text)
    {
        // Only scan the first ~200 characters — the name always appears at the top
        var head = text.Length > 200 ? text[..200] : text;
        var m = RxSenderName.Match(head);
        return m.Success ? m.Groups[1].Value.Trim() : string.Empty;
    }

    private static string CleanInput(string text)
    {
        // Word-bounded removal for actual ASCII words only — Swedish greetings
        // are not relevant here but the word list is ASCII so \b is safe.
        foreach (var word in new[] { "morning", "hello", "hi" })
            text = Regex.Replace(text, @"\b" + Regex.Escape(word) + @"\b", string.Empty, RegexOptions.IgnoreCase);

        // Keep all Unicode word characters (covers å/ä/ö for Swedish keywords
        // such as "köpa"/"sälja" matched by RxLeg), plus whitespace and the
        // punctuation that date/number formats rely on.
        // \w in .NET's default (non-ECMAScript) mode matches Unicode letters,
        // digits and _ — Swedish chars are preserved. Only stray ASCII punctuation
        // such as "!" is stripped as a side-effect.
        return Regex.Replace(text, @"[^\w\s.,/\-:+]", string.Empty);
    }

    private string ExtractPair(string text)
    {
        // ── 1. Allowlist match (most reliable) ──────────────────────────────
        if (_knownPairs.Count > 0)
        {
            foreach (Match m in RxPairCompact.Matches(text))
            {
                var candidate = m.Groups[1].Value.ToUpperInvariant();
                if (_knownPairs.Contains(candidate))
                    return candidate;
            }

            foreach (Match m in RxPairSpaced.Matches(text))
            {
                var candidate = (m.Groups[1].Value + m.Groups[2].Value).ToUpperInvariant();
                if (_knownPairs.Contains(candidate))
                    return candidate;
            }

            foreach (Match m in RxPairSlash.Matches(text))
            {
                var candidate = (m.Groups[1].Value + m.Groups[2].Value).ToUpperInvariant();
                if (_knownPairs.Contains(candidate))
                    return candidate;
            }

            // Allowlist loaded but no match found — do not fall through to
            // the unchecked fallback; return UNKNOWN so the caller falls back to AI.
            return "UNKNOWN";
        }

        // ── 2. Fallback (no reference data available yet) ────────────────────
        var mSlash = RxPairSlash.Match(text);
        if (mSlash.Success)
            return (mSlash.Groups[1].Value + mSlash.Groups[2].Value).ToUpperInvariant();

        var mc = RxPairCompact.Match(text);
        if (mc.Success)
            return mc.Groups[1].Value.ToUpperInvariant();

        var ms = RxPairSpaced.Match(text);
        return ms.Success
            ? (ms.Groups[1].Value + ms.Groups[2].Value).ToUpperInvariant()
            : "UNKNOWN";
    }

    private static string ExtractSpot(string text)
    {
        var m = RxSpot.Match(text);
        if (!m.Success) return string.Empty;

        var norm = NormalizeFxNumber(m.Groups[1].Value);
        if (!decimal.TryParse(norm, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
            return string.Empty;

        var s = val.ToString(CultureInfo.InvariantCulture);
        return s.Contains('.') ? s.TrimEnd('0').TrimEnd('.') : s;
    }

    private static string ExtractExpiryOrTenor(string text)
    {
        var m = RxExpiry.Match(text);
        if (m.Success)
        {
            int day = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            string mn = m.Groups[2].Value;
            bool yearExplicit = m.Groups[3].Success;

            // Use UTC date consistently to avoid local-midnight / year-boundary
            // drift when the server is in a different timezone.
            var today = DateTime.UtcNow.Date;
            int year = yearExplicit ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : today.Year;

            if (DateTime.TryParseExact($"{day} {mn} {year}", "d MMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ||
                DateTime.TryParse($"{day} {mn} {year}", out dt))
            {
                // If no year was specified and the resolved date is already in the past,
                // roll forward one year — Bloomberg messages often omit the year.
                if (!yearExplicit && dt < today)
                    dt = dt.AddYears(1);

                return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }

        var t = RxTenor.Match(text);
        return t.Success ? t.Groups[1].Value.ToUpperInvariant() : string.Empty;
    }

    private static List<OvmlLeg> ExtractLegs(string cleaned, string pair, string expiry, string spot)
    {
        var list = new List<OvmlLeg>();

        foreach (Match m in RxLeg.Matches(cleaned))
        {
            var action = m.Groups[1].Value.ToLowerInvariant();
            var buySell = (action.StartsWith('b') || action.StartsWith('k')) ? "Buy" : "Sell";
            var putCall = m.Groups[3].Value.ToLowerInvariant() switch { "call" => "Call", "put" => "Put", _ => string.Empty };
            // Delta strikes stored as uppercase display form: "25d" → "25D"
            var rawStrike = TrimZeros(m.Groups[2].Value);
            var strike = RxDeltaStrike.IsMatch(rawStrike)
                ? rawStrike.ToUpperInvariant()
                : rawStrike;
            var notional = string.IsNullOrEmpty(m.Groups[4].Value) ? 0L : ConvertNotional(m.Groups[4].Value);

            list.Add(new OvmlLeg(pair, buySell, putCall, strike, notional, expiry, spot,
                SpotFromParsing: !string.IsNullOrEmpty(spot)));
        }

        return list;
    }

    private static bool IsFullyPopulated(string pair, string expiry, string spot, List<OvmlLeg> legs)
    {
        if (string.IsNullOrWhiteSpace(pair) || pair.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(expiry)) return false;
        if (string.IsNullOrWhiteSpace(spot)) return false;
        if (legs is not { Count: >= 1 and <= 3 }) return false;

        foreach (var l in legs)
        {
            if (string.IsNullOrWhiteSpace(l.BuySell)) return false;
            if (string.IsNullOrWhiteSpace(l.PutCall)) return false;
            if (string.IsNullOrWhiteSpace(l.Strike)) return false;
            if (l.Notional <= 0) return false;
        }

        return true;
    }

    private static string BuildOvml(string pair, string expiry, string spot, List<OvmlLeg> legs)
    {
        // Normalise expiry: yyyy-MM-dd → MM/dd/yy for OVML syntax
        if (Regex.IsMatch(expiry, @"^\d{4}-\d{2}-\d{2}$") &&
            DateTime.TryParseExact(expiry, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            expiry = dt.ToString("MM/dd/yy", CultureInfo.InvariantCulture);
        }

        // Normalise spot: ensure decimal separator is always a dot (AI parser may return comma)
        spot = spot.Replace(',', '.');

        var buySells = string.Join(",", legs.Select(l => l.BuySell.StartsWith('B') ? "B" : "S"));
        var strikes = string.Join(",", legs.Select(l =>
        {
            var formatted = FormatStrike(l.Strike);
            if (string.IsNullOrEmpty(l.PutCall)) return formatted;
            var cp = l.PutCall.Equals("Call", StringComparison.OrdinalIgnoreCase) ? "C" : "P";
            return formatted.StartsWith("DF", StringComparison.OrdinalIgnoreCase)
                ? $"{cp} {formatted}"
                : $"{cp}{formatted}";
        }));

        var priceCcy = pair.Length == 6 ? pair[3..].ToUpperInvariant() : string.Empty;

        var parts = new List<string> { "OVML", pair, buySells, strikes };

        // Only add notional token when at least one leg has a non-zero value
        if (legs.Any(l => l.Notional > 0))
        {
            var notionalValues = legs.Select(l => ConvertToOvmlNotional(l.Notional)).ToList();
            var notionals = "N" + (notionalValues.Distinct().Count() == 1
                ? notionalValues[0]
                : string.Join(",", notionalValues));
            parts.Add(notionals);
        }

        parts.Add(expiry);
        if (!string.IsNullOrEmpty(priceCcy)) parts.Add("PC" + priceCcy);
        if (!string.IsNullOrEmpty(spot)) parts.Add("SP" + spot);

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Converts a delta-form strike (e.g. "25D") to OVML delta syntax "DF25".
    /// Absolute strikes (e.g. "1.0850") are returned unchanged.
    /// </summary>
    private static string FormatStrike(string strike)
    {
        if (string.IsNullOrEmpty(strike)) return strike;

        var m = RxDeltaStrike.Match(strike);
        if (!m.Success) return strike;

        var number = TrimZeros(m.Groups[1].Value);
        return $"DF{number}";
    }

    // ── Numeric helpers ──────────────────────────────────────────────────────

    private static string NormalizeFxNumber(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        raw = raw.Replace(" ", string.Empty);

        int commas = raw.Count(c => c == ',');
        int dots = raw.Count(c => c == '.');

        if (commas > 0 && dots > 0)
        {
            raw = raw.LastIndexOf('.') > raw.LastIndexOf(',')
                ? raw.Replace(",", string.Empty)
                : raw.Replace(".", string.Empty).Replace(",", ".");
        }
        else if (commas == 1)
        {
            var parts = raw.Split(',');
            raw = parts[1].Length == 3 ? parts[0] + parts[1] : parts[0] + "." + parts[1];
        }
        else if (dots > 1)
        {
            int last = raw.LastIndexOf('.');
            raw = raw[..last].Replace(".", string.Empty) + "." + raw[(last + 1)..];
        }

        return raw;
    }

    private static string TrimZeros(string s)
    {
        if (string.IsNullOrEmpty(s) || !s.Contains('.')) return s;
        return s.TrimEnd('0').TrimEnd('.');
    }

    private static long ConvertNotional(string s)
    {
        s = s.Replace(" ", string.Empty).Replace(",", ".");
        var m = Regex.Match(s, @"([0-9]*\.?[0-9]+)");
        if (!m.Success) return 0;
        return double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
            ? (long)(v * 1_000_000)
            : 0;
    }

    private static string ConvertToOvmlNotional(long notional) =>
        notional % 1_000_000 == 0 ? $"{notional / 1_000_000}M" : notional.ToString();
}
