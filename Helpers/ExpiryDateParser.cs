using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FxTradeConfirmation.Helpers;

/// <summary>
/// Parses user input for expiry dates into a resolved Convention using DateConvention.
/// Accepted formats:
///   Tenor:    "on", "o/n", "1d", "2d", "1w", "2w", "1m", "3m", "1y", "2y"
///   Date:     "dd/MM", "dd/MM/yyyy", "dd-MMM" (e.g. "15-mar", "20-dec"), "yyyy-MM-dd"
/// Returns null if input cannot be parsed.
/// </summary>
public static class ExpiryDateParser
{
    private static readonly Regex TenorRegex = new(
        @"^(\d+)\s*([dwmy])$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] MonthAbbreviations =
        ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    /// <summary>
    /// Attempts to parse user input into a DateConvention tenor string or a date string
    /// that DateConvention.GetConvention() can process.
    /// </summary>
    /// <param name="input">Raw user input.</param>
    /// <param name="currencyPair">Currency pair (e.g. "EURSEK") for DateConvention.</param>
    /// <param name="holidays">Holiday DataTable from HolidayDAO.</param>
    /// <returns>Resolved Convention, or null if input is invalid.</returns>
    public static Convention? Parse(string input, string currencyPair, DataTable holidays)
    {
        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(currencyPair) || currencyPair.Length < 6)
            return null;

        var trimmed = input.Trim().ToLowerInvariant();

        try
        {
            var dc = new DateConvention(currencyPair, holidays);

            // Overnight: "on" or "o/n"
            if (trimmed is "on" or "o/n")
                return dc.GetConvention("on");

            // Tenor: "1d", "2w", "3m", "1y" etc.
            var tenorMatch = TenorRegex.Match(trimmed);
            if (tenorMatch.Success)
                return dc.GetConvention(trimmed);

            // Try to parse as a date in various formats, then pass to GetConvention
            DateTime? parsedDate = TryParseDate(trimmed);
            if (parsedDate.HasValue)
                return dc.GetConvention(parsedDate.Value.ToString("yyyy-MM-dd"));

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Tries to parse the input as a date using multiple formats.
    /// </summary>
    private static DateTime? TryParseDate(string input)
    {
        // yyyy-MM-dd (already formatted)
        if (DateTime.TryParseExact(input, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var d1))
            return d1;

        // dd/MM/yyyy
        if (DateTime.TryParseExact(input, "d/M/yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var d2))
            return d2;

        // dd/MM (assume current or next year)
        if (DateTime.TryParseExact(input, "d/M", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var d3))
        {
            // .NET defaults to year 1 for d/M, so fix the year
            var candidate = new DateTime(DateTime.Today.Year, d3.Month, d3.Day);
            if (candidate < DateTime.Today)
                candidate = candidate.AddYears(1);
            return candidate;
        }

        // dd-MMM (e.g. "15-mar", "20-dec") — month as 3-letter text
        var dashIdx = input.IndexOf('-');
        if (dashIdx > 0 && dashIdx < input.Length - 1)
        {
            var dayPart = input[..dashIdx];
            var monthPart = input[(dashIdx + 1)..].Trim().ToLowerInvariant();

            if (int.TryParse(dayPart, out int day) && day >= 1 && day <= 31)
            {
                int monthIndex = Array.IndexOf(MonthAbbreviations, monthPart);
                if (monthIndex >= 0)
                {
                    int month = monthIndex + 1;
                    try
                    {
                        var candidate = new DateTime(DateTime.Today.Year, month, day);
                        if (candidate < DateTime.Today)
                            candidate = candidate.AddYears(1);
                        return candidate;
                    }
                    catch
                    {
                        return null; // invalid day for that month
                    }
                }
            }
        }

        // Fallback: try general DateTime.TryParse
        if (DateTime.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dFallback))
            return dFallback;

        return null;
    }
}