using FxTradeConfirmation.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FxTradeConfirmation.Helpers;

/// <summary>
/// Parses compact trade shorthand into one or more <see cref="OvmlLeg"/> objects.
/// Only the currency pair is required; all other tokens are optional.
/// <para>
/// Examples:<br/>
/// <c>eursek 1w</c><br/>
/// <c>eursek 25/4</c> — day/month without year, rolls to next year if past<br/>
/// <c>eursek 1w,1m b,s c,p</c><br/>
/// <c>eursek 1m b,s c11,p10.5 n10m,5m</c><br/>
/// <c>eursek b c11 n5m</c><br/>
/// <c>eursek 1m s10.30 c11 n5m</c> — spot reference → Hedge=Spot, HedgeRate=10.30
/// </para>
/// </summary>
public static class QuickInputParser
{
    public sealed record ParseResult(IReadOnlyList<OvmlLeg> Legs, string? Error);

    // Tenor: 1W, 2M, 3D, 1Y
    private static readonly Regex RxTenor = new(
        @"^\d+[WMDY]$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Full date with three parts: 2025-06-20, 20/06/25, 06/20/2025
    private static readonly Regex RxFullDate = new(
        @"^\d{1,4}[-/]\d{1,2}[-/]\d{1,4}$",
        RegexOptions.Compiled);

    // Day/month without year: 25/4, 4/25, 25.4
    private static readonly Regex RxDayMonth = new(
        @"^(\d{1,2})[/.](\d{1,2})$",
        RegexOptions.Compiled);

    // Day + month name: 20jun, 20jun25, 20june2025
    private static readonly Regex RxDayMonthName = new(
        @"^\d{1,2}[A-Za-z]{3,9}\d{0,4}$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Notional — N prefix is mandatory: N10M, N500K, N1B, N25000000
    private static readonly Regex RxNotional = new(
        @"^N(\d+(?:[.,]\d+)?)[MKBY]?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Comma-separated buy/sell list: b, s, b,s, buy,sell
    private static readonly Regex RxBuySell = new(
        @"^(b(?:uy)?|s(?:ell)?|k(?:öpa)?|sälja)(?:,(b(?:uy)?|s(?:ell)?|k(?:öpa)?|sälja))*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Comma-separated call/put + optional strike: c, p, c11, p10.5, c11,p10.5, c,p
    private static readonly Regex RxStrikes = new(
        @"^[CP]?\d*(?:[.,]\d+)?D?(?:,[CP]?\d*(?:[.,]\d+)?D?)*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Spot reference: SP followed by a numeric value, e.g. SP10.30 or SP10,30
    private static readonly Regex RxSpot = new(
        @"^SP\d+(?:[.,]\d+)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ParseResult Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new ParseResult([], "Empty input");

        var tokens = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int idx = 0;

        // ── 1. Currency pair (required, first token) ────────────────────────
        var pair = tokens[idx++].Replace("/", "").ToUpperInvariant();
        if (pair.Length != 6)
            return new ParseResult([], $"Invalid currency pair: '{pair}'");

        // ── 2. Classify remaining tokens ────────────────────────────────────
        var expiries = new List<string>();
        var buySells = new List<string>();
        var callPuts = new List<string>();
        var strikes = new List<string>();
        var notionals = new List<long>();
        string spotRef = string.Empty;

        while (idx < tokens.Length)
        {
            var token = tokens[idx++];

            // Tenor/date list: 1w  |  1w,1m  |  2025-06-20  |  25/4  |  25/4,20/6
            if (IsTenorOrDateList(token))
            {
                foreach (var part in token.Split(','))
                    expiries.Add(NormalizeExpiry(part.Trim()));
                continue;
            }

            // Spot reference: S10.30 or S10,30 — must be checked BEFORE Buy/Sell
            // because "S" alone means Sell, but "S" + digits means Spot.
            if (RxSpot.IsMatch(token))
            {
                spotRef = token[2..].Replace(',', '.');
                continue;
            }

            // Buy/Sell list: b | s | b,s | buy,sell
            if (RxBuySell.IsMatch(token))
            {
                foreach (var part in token.Split(','))
                {
                    var p = part.Trim().ToUpperInvariant();
                    buySells.Add(p is "S" or "SELL" or "SÄLJA" ? "Sell" : "Buy");
                }
                continue;
            }

            // Call/Put + optional strike: c | p | c,p | c11 | p10.5 | c11,p10.5
            if (RxStrikes.IsMatch(token) && HasCallPutPrefix(token))
            {
                foreach (var part in SplitStrikeParts(token))
                {
                    ParseStrikePart(part, out var callPut, out var strike);
                    callPuts.Add(callPut);
                    strikes.Add(strike);
                }
                continue;
            }

            // Notional list — N prefix required: n10m | n10m,5m | n1m,6m
            if (IsNotionalList(token))
            {
                foreach (var part in token.Split(','))
                {
                    var n = ParseNotional(part.Trim());
                    if (n > 0)
                        notionals.Add(n);
                }
                continue;
            }

            // Unknown token — silently skip
        }

        // ── 3. Resolve leg count ─────────────────────────────────────────────
        int legCount = Math.Max(1,
            new[] { expiries.Count, buySells.Count, callPuts.Count, strikes.Count, notionals.Count }.Max());

        // Single value → broadcast to all legs; multiple → pad with neutral default
        string sharedExpiry = expiries.Count == 1 ? expiries[0] : string.Empty;
        long sharedNotional = notionals.Count == 1 ? notionals[0] : 0;

        while (expiries.Count < legCount) expiries.Add(sharedExpiry);
        while (buySells.Count < legCount) buySells.Add("Buy");
        while (callPuts.Count < legCount) callPuts.Add("Call");
        while (strikes.Count < legCount) strikes.Add(string.Empty);
        while (notionals.Count < legCount) notionals.Add(sharedNotional);

        // ── 4. Build OvmlLeg list ────────────────────────────────────────────
        var legs = new List<OvmlLeg>();
        for (int i = 0; i < legCount; i++)
        {
            legs.Add(new OvmlLeg(
                Pair: pair,
                BuySell: buySells[i],
                PutCall: callPuts[i],
                Strike: strikes[i],
                Notional: notionals[i],
                Expiry: expiries[i],
                Spot: i == 0 ? spotRef : string.Empty   // spot only on first leg
            ));
        }

        return new ParseResult(legs, null);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsTenorOrDateList(string token)
    {
        var parts = token.Split(',');
        return parts.Length > 0 && parts.All(p => IsTenorOrDate(p.Trim()));
    }

    private static bool IsTenorOrDate(string s) =>
        RxTenor.IsMatch(s) ||
        RxFullDate.IsMatch(s) ||
        RxDayMonth.IsMatch(s) ||
        RxDayMonthName.IsMatch(s);

    /// <summary>
    /// Returns true when the token starts with N and every comma-part
    /// is a valid notional value (e.g. n10m, n10m,5m, n1m,6m).
    /// The first part must include the N prefix; subsequent parts may omit it.
    /// </summary>
    private static bool IsNotionalList(string token)
    {
        if (token.Length == 0 || char.ToUpperInvariant(token[0]) != 'N')
            return false;

        // Strip leading N from the first part only, then test all parts
        var parts = token.Split(',');
        var firstStripped = parts[0].Trim()[1..]; // remove the N

        if (!IsNotionalValue(firstStripped))
            return false;

        for (int i = 1; i < parts.Length; i++)
        {
            var p = parts[i].Trim();
            // Subsequent parts may optionally carry their own N prefix
            var value = p.Length > 0 && char.ToUpperInvariant(p[0]) == 'N' ? p[1..] : p;
            if (!IsNotionalValue(value))
                return false;
        }

        return true;
    }

    private static bool IsNotionalValue(string s) =>
        s.Length > 0 && RxNotional.IsMatch("N" + s);

    private static string NormalizeExpiry(string token)
    {
        // Tenor — pass through unchanged
        if (RxTenor.IsMatch(token))
            return token.ToUpperInvariant();

        // Day/month without year: 25/4 or 25.4
        var dmMatch = RxDayMonth.Match(token);
        if (dmMatch.Success &&
            int.TryParse(dmMatch.Groups[1].Value, out int a) &&
            int.TryParse(dmMatch.Groups[2].Value, out int b))
        {
            // European convention: ambiguous → treat as day/month
            var (day, month) = a <= 12 && b > 12 ? (b, a)
                             : a > 12 ? (a, b)
                             : (a, b);
            return ResolveYearRoll(day, month);
        }

        // Full date or day+month-name — let ExpiryDateParser handle normalisation
        return token;
    }

    private static string ResolveYearRoll(int day, int month)
    {
        var today = DateTime.UtcNow.Date;
        try
        {
            var candidate = new DateTime(today.Year, month, day);
            if (candidate < today)
                candidate = candidate.AddYears(1);

            return candidate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch
        {
            return $"{today.Year}-{month:D2}-{day:D2}";
        }
    }

    private static bool HasCallPutPrefix(string token)
    {
        var first = token.Split(',')[0].Trim();
        return first.Length > 0 && char.ToUpperInvariant(first[0]) is 'C' or 'P';
    }

    /// <summary>
    /// Splits a strike token on commas, but treats a comma as a decimal separator
    /// (not a leg separator) when the part after the comma does NOT start with C or P.
    /// <para>
    /// <c>"c11,p10.5"</c> → <c>["c11", "p10.5"]</c> (two legs)<br/>
    /// <c>"P10,50"</c>    → <c>["P10.50"]</c>        (one leg, decimal comma)<br/>
    /// <c>"c11,50"</c>    → <c>["c11.50"]</c>        (one leg, decimal comma)
    /// </para>
    /// </summary>
    private static List<string> SplitStrikeParts(string token)
    {
        var rawParts = token.Split(',');
        var result = new List<string>();

        int i = 0;
        while (i < rawParts.Length)
        {
            var current = rawParts[i].Trim();

            // Peek at the next part: if it exists and does NOT start with C/P,
            // the comma was a decimal separator → merge with current part.
            if (i + 1 < rawParts.Length)
            {
                var next = rawParts[i + 1].Trim();
                if (next.Length > 0 && char.ToUpperInvariant(next[0]) is not ('C' or 'P'))
                {
                    // Merge: replace the comma with a dot for decimal
                    result.Add(current + "." + next);
                    i += 2;
                    continue;
                }
            }

            result.Add(current);
            i++;
        }

        return result;
    }

    private static void ParseStrikePart(string part, out string callPut, out string strike)
    {
        if (part.Length == 0)
        {
            callPut = "Call";
            strike = string.Empty;
            return;
        }

        var first = char.ToUpperInvariant(part[0]);
        if (first is 'C' or 'P')
        {
            callPut = first is 'P' ? "Put" : "Call";
            strike = part.Length > 1 ? part[1..] : string.Empty;
        }
        else
        {
            callPut = "Call";
            strike = part;
        }
    }

    private static long ParseNotional(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return 0;

        input = input.Trim().Replace(" ", "");

        // Strip leading N — always present on first part, optional on subsequent parts
        if (input.Length > 1 && char.ToUpperInvariant(input[0]) == 'N')
            input = input[1..];

        char lastChar = char.ToUpperInvariant(input[^1]);
        long multiplier = lastChar switch
        {
            'M' => 1_000_000,
            'K' => 1_000,
            'B' => 1_000_000_000,
            'Y' => 1_000_000_000,
            _ => 1
        };

        var numPart = multiplier != 1 ? input[..^1] : input;
        numPart = numPart.Replace(',', '.');

        return double.TryParse(numPart, System.Globalization.NumberStyles.Any,
            CultureInfo.InvariantCulture, out var value)
            ? (long)(value * multiplier)
            : 0;
    }
}
