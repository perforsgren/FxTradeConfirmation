using System.Globalization;

namespace FxTradeConfirmation.Helpers;

/// <summary>
/// Parses notional input with M/K/Y suffixes.
/// "5M" → 5,000,000 | "100K" → 100,000 | "2Y" → 2,000,000,000
/// </summary>
public static class NotionalParser
{
    public static decimal? Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        input = input.Trim().Replace(" ", "");

        if (input.Length == 0) return null;

        char lastChar = char.ToUpperInvariant(input[^1]);
        decimal multiplier = lastChar switch
        {
            'M' => 1_000_000m,
            'K' => 1_000m,
            'Y' => 1_000_000_000m,
            _ => 1m
        };

        string numPart = multiplier != 1m ? input[..^1] : input;

        if (decimal.TryParse(numPart, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            return value * multiplier;

        return null;
    }

    public static string Format(decimal? value)
    {
        if (!value.HasValue) return string.Empty;
        return value.Value.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ");
    }
}
