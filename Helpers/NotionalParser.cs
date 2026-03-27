using System.Globalization;

namespace FxTradeConfirmation.Helpers;

/// <summary>
/// Parses notional input with M/K/B/Y suffixes.
/// "5M" → 5,000,000 | "100K" → 100,000 | "2B" → 2,000,000,000 | "1Y" → 1,000,000,000
/// Comma typed by user is treated as decimal separator.
/// Commas in formatted display values are thousands separators and are stripped.
/// </summary>
public static class NotionalParser
{
    public static decimal? Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        input = input.Trim().Replace(" ", "");

        if (input.Length == 0) return null;

        // Detect suffix before normalizing
        char lastChar = char.ToUpperInvariant(input[^1]);
        decimal multiplier = lastChar switch
        {
            'M' => 1_000_000m,
            'K' => 1_000m,
            'B' => 1_000_000_000m,
            'Y' => 1_000_000_000m,
            _ => 1m
        };

        string numPart = multiplier != 1m ? input[..^1] : input;

        // Determine whether commas are thousands separators or a decimal separator.
        // If there is exactly one comma AND it is followed by 1-2 digits (or digits + suffix was stripped),
        // OR there are no dots, treat the single comma as a decimal separator.
        // If there are multiple commas, they are thousands separators.
        int commaCount = numPart.Count(c => c == ',');
        int dotCount = numPart.Count(c => c == '.');

        if (commaCount == 1 && dotCount == 0)
        {
            // A single comma followed by exactly 1 or 2 digits is a decimal separator (e.g. "1,5" or "1,25").
            // A single comma followed by exactly 3 digits is a thousands separator (e.g. "500,000").
            int commaIndex = numPart.IndexOf(',');
            int digitsAfterComma = numPart.Length - commaIndex - 1;

            if (digitsAfterComma == 3)
                numPart = numPart.Replace(",", "");      // thousands separator → strip
            else
                numPart = numPart.Replace(',', '.');     // decimal separator → replace
        }
        else if (commaCount > 0)
        {
            // Multiple commas or commas with dots → commas are thousands separators, strip them
            numPart = numPart.Replace(",", "");
        }
        // If no commas, nothing to do (dots are already fine for InvariantCulture)

        if (numPart.Length == 0) return null;

        if (decimal.TryParse(numPart, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            return value * multiplier;

        return null;
    }

    /// <summary>
    /// Formats a notional value with comma as thousands separator.
    /// Uses zero decimals by default. Only shows decimals if the expanded result
    /// has a fractional part, or if the caller explicitly requests a minimum.
    /// </summary>
    public static string Format(decimal? value, int? userDecimals = null)
    {
        if (!value.HasValue) return string.Empty;

        var v = value.Value;

        // Count actual fractional decimals in the expanded result
        var raw = v.ToString("G29", CultureInfo.InvariantCulture);
        int dotIndex = raw.IndexOf('.');
        int actualDecimals = dotIndex >= 0 ? raw.Length - dotIndex - 1 : 0;

        int decimals;
        if (userDecimals.HasValue)
            decimals = Math.Max(actualDecimals, userDecimals.Value);
        else
            decimals = actualDecimals;

        // N format in InvariantCulture uses comma as thousands separator
        return v.ToString($"N{decimals}", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Counts the number of decimal places the user typed in the raw input.
    /// Returns null if no decimal point was present.
    /// For suffix inputs (e.g. "1.5m"), returns decimals only if the
    /// expanded result actually has a fractional part.
    /// </summary>
    public static int? CountInputDecimals(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var normalized = input.Trim().Replace(" ", "");

        // Strip suffix
        bool hasSuffix = false;
        if (normalized.Length > 0)
        {
            char last = char.ToUpperInvariant(normalized[^1]);
            if (last is 'M' or 'K' or 'B' or 'Y')
            {
                hasSuffix = true;
                normalized = normalized[..^1];
            }
        }

        // Determine the decimal separator position
        // Same logic as Parse: single comma with no dots = decimal separator
        int commaCount = normalized.Count(c => c == ',');
        int dotCount = normalized.Count(c => c == '.');

        if (commaCount == 1 && dotCount == 0)
            normalized = normalized.Replace(',', '.');
        else if (commaCount > 0)
            normalized = normalized.Replace(",", "");

        int dotIndex = normalized.LastIndexOf('.');
        if (dotIndex < 0) return null;

        int inputDecimals = normalized.Length - dotIndex - 1;

        // For suffix inputs, only return user decimals if the expanded result
        // would actually have a fractional part.
        if (hasSuffix)
        {
            var parsed = Parse(input);
            if (parsed.HasValue)
            {
                var raw = parsed.Value.ToString("G29", CultureInfo.InvariantCulture);
                int resultDot = raw.IndexOf('.');
                int resultDecimals = resultDot >= 0 ? raw.Length - resultDot - 1 : 0;
                if (resultDecimals == 0)
                    return null;
                return resultDecimals;
            }
        }

        return inputDecimals;
    }
}