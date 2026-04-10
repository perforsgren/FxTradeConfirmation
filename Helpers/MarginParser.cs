using System.Globalization;

namespace FxTradeConfirmation.Helpers;

/// <summary>
/// Parses margin input. Only k/K (thousands) and m/M (millions) suffixes are accepted.
/// All other non-numeric input is rejected (returns null).
/// Comma typed by user is treated as decimal separator.
/// Display value uses comma as thousands separator and always 2 decimal places.
/// </summary>
public static class MarginParser
{
    /// <summary>
    /// Parses the margin input string. Returns the numeric value or null if invalid.
    /// Accepts: plain numbers, k/K suffix (×1,000), m/M suffix (×1,000,000).
    /// Comma is treated as decimal separator when it's the only separator.
    /// </summary>
    public static decimal? Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        input = input.Trim().Replace(" ", "");

        if (input.Length == 0) return null;

        // Detect suffix
        char lastChar = char.ToUpperInvariant(input[^1]);
        decimal multiplier = lastChar switch
        {
            'K' => 1_000m,
            'M' => 1_000_000m,
            _ => 1m
        };

        string numPart = multiplier != 1m ? input[..^1] : input;

        if (numPart.Length == 0) return null;

        // If the remaining part (after stripping suffix) contains any non-numeric
        // characters other than comma, dot, minus, and plus → reject
        foreach (char c in numPart)
        {
            if (!char.IsDigit(c) && c != ',' && c != '.' && c != '-' && c != '+')
                return null;
        }

        // Determine whether commas are thousands separators or a decimal separator
        int commaCount = numPart.Count(c => c == ',');
        int dotCount = numPart.Count(c => c == '.');

        if (commaCount == 1 && dotCount == 0)
        {
            // Single comma, no dots → comma is the decimal separator
            numPart = numPart.Replace(',', '.');
        }
        else if (commaCount > 0)
        {
            // Multiple commas or commas with dots → commas are thousands separators
            numPart = numPart.Replace(",", "");
        }

        if (decimal.TryParse(numPart, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            return value * multiplier;

        return null;
    }

    /// <summary>
    /// Formats a margin value with comma as thousands separator and exactly 2 decimal places.
    /// </summary>
    public static string Format(decimal value)
        => value.ToString("N2", CultureInfo.InvariantCulture);
}
