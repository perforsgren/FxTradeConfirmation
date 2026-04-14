using System.Data;
using System.Globalization;

namespace FxTradeConfirmation.Helpers;

public class Convention
{
    public DateTime SpotDate { get; set; }
    public DateTime ExpiryDate { get; set; }
    public DateTime DeliveryDate { get; set; }
    public int Days { get; set; }
}

public class DateConvention
{
    // Replaced parallel arrays with a dictionary — eliminates index-coupling fragility
    // and corrects the "Calender" typo. Keys are ISO currency codes; values are calendar names.
    internal static readonly Dictionary<string, string> CurrencyToCalendar =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["EUR"] = "TARGET",
            ["USD"] = "USA",
            ["SEK"] = "SWEDEN",
            ["NOK"] = "NORWAY",
            ["GBP"] = "ENGLAND",
            ["CAD"] = "CANADA",
            ["CHF"] = "SWITZERLAND",
            ["AUD"] = "AUSTRALIA",
            ["RUB"] = "RUSSIA",
            ["JPY"] = "JAPAN",
            ["DKK"] = "DENMARK",
            ["HKD"] = "HONGKONG",
            ["SGD"] = "SINGAPORE",
            ["TRY"] = "TURKEY",
            ["PLN"] = "POLAND",
            ["HUF"] = "HUNGARY",
            ["CZK"] = "CZECHREPUBLIC",
            ["NZD"] = "NEWZEALAND",
        };

    // Removed = new DataTable() initialiser — the constructor overwrites it immediately,
    // so the allocation was wasted. Field is now null-initialised and always set in ctor.
    private DataTable _holidays;
    private string _ccy;
    private int _tAdd;

    // NOTE: _spotDate, _expiryDate, _deliveryDate, _days removed —
    // they were written and read only within GetConvention, making
    // them de-facto local variables stored as fields. Concurrent calls
    // on a shared instance would corrupt each other's intermediate state.

    private Calendar _calendar;

    /// <summary>Cached USA calendar — avoids re-filtering the holidays DataTable on every call (Bug #3).</summary>
    private Calendar _usaCalendar;

    /// <summary>Cached non-US leg calendar — avoids re-filtering the holidays DataTable on every call (Bug #4).</summary>
    private Calendar _nonUsCalendar;

    /// <summary>
    /// Maximum number of calendar days any date-rolling loop may advance or retreat.
    /// No legitimate FX settlement roll exceeds this bound; a higher count indicates
    /// corrupt or missing holiday data.
    /// </summary>
    private const int MaxRollDays = 30;

    public DateConvention(string CCY, DataTable Holidays)
    {
        _holidays = Holidays;
        _ccy = CCY;

        if (_ccy == "USDCAD" || _ccy == "USDTRY" || _ccy == "USDPHP" ||
            _ccy == "USDRUB" || _ccy == "USDKZT" || _ccy == "USDPKR")
        {
            _tAdd = 1;
        }
        else
        {
            _tAdd = 2;
        }

        Calendar Calendar = new Calendar(ctryNames(CCY), Holidays);
        _calendar = Calendar;

        // Bug #3 fix: cache a USA calendar once instead of rebuilding per call.
        _usaCalendar = new Calendar("USA", Holidays);

        // Bug #4 fix: cache the non-US leg calendar once instead of rebuilding per call.
        _nonUsCalendar = BuildNonUsCalendar(CCY, Holidays);
    }

    /// <summary>T+N settlement offset for this currency pair (1 for USDCAD/USDTRY/etc., 2 for all others).</summary>
    public int TAdd => _tAdd;

    /// <summary>
    /// Builds a Calendar that contains holidays for the CCY legs excluding the USA leg.
    /// Used by <see cref="isCCYHoliday_notUS"/> to avoid allocating a new Calendar per call.
    /// </summary>
    private static Calendar BuildNonUsCalendar(string ccy, DataTable holidays)
    {
        string[] ctryCal = ctryNames(ccy);

        string ctry1;
        string ctry2;

        if (ctryCal[0] == "USA")
        {
            ctry1 = ctryCal[1];
            ctry2 = "";
        }
        else if (ctryCal[1] == "USA")
        {
            ctry1 = ctryCal[0];
            ctry2 = "";
        }
        else
        {
            ctry1 = ctryCal[0];
            ctry2 = ctryCal[1];
        }

        return new Calendar(new string[] { ctry1, ctry2 }, holidays);
    }

    public Convention GetConvention(string timeToExpiry)
    {
        DateTime horizonDate = DateTime.Today;

        DateTime spotDate;
        DateTime expiryDate;
        DateTime deliveryDate;

        // Over-night
        if (timeToExpiry.ToLower() == "on")
        {
            spotDate = getForwardDate(horizonDate, _tAdd);
            expiryDate = moveBusinessDays(horizonDate, 1);

            if (isFirstOfJan(expiryDate) == true)
            {
                expiryDate = moveBusinessDays(expiryDate, 1);
            }

            deliveryDate = getForwardDate(expiryDate, _tAdd);
        }

        // days
        else if (timeToExpiry.ToLower().EndsWith("d"))
        {
            spotDate = getForwardDate(horizonDate, _tAdd);
            string dStr = timeToExpiry.ToLower().Replace("d", "");
            if (!double.TryParse(dStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double daysToExpiry))
                throw new FormatException($"Cannot parse day count '{dStr}' in tenor '{timeToExpiry}'.");
            expiryDate = horizonDate.AddDays(daysToExpiry);

            if (isFirstOfJan(expiryDate) == true)
            {
                expiryDate = moveBusinessDays(expiryDate, 1);
            }
            deliveryDate = getForwardDate(expiryDate, _tAdd);
        }

        // weeks
        else if (timeToExpiry.ToLower().EndsWith("w"))
        {
            spotDate = getForwardDate(horizonDate, _tAdd);

            string wStr = timeToExpiry.ToLower().Replace("w", "");
            if (!int.TryParse(wStr, NumberStyles.None, CultureInfo.InvariantCulture, out int weeks))
                throw new FormatException($"Cannot parse week count '{wStr}' in tenor '{timeToExpiry}'.");
            expiryDate = horizonDate.AddDays(7 * weeks);

            if (isFirstOfJan(expiryDate) == true)
            {
                expiryDate = moveBusinessDays(expiryDate, 1);
            }
            deliveryDate = getForwardDate(expiryDate, _tAdd);
        }

        // months
        else if (timeToExpiry.ToLower().EndsWith("m"))
        {
            string monthString = timeToExpiry.Substring(0, timeToExpiry.Length - 1);
            if (!int.TryParse(monthString, NumberStyles.None, CultureInfo.InvariantCulture, out int months))
                throw new FormatException($"Cannot parse month count '{monthString}' in tenor '{timeToExpiry}'.");
            spotDate = getForwardDate(horizonDate, _tAdd);
            deliveryDate = addMonths(spotDate, months);
            expiryDate = getBackwardDate(deliveryDate, _tAdd);
        }

        // years
        else if (timeToExpiry.ToLower().EndsWith("y"))
        {
            string yearString = timeToExpiry.Substring(0, timeToExpiry.Length - 1);
            if (!int.TryParse(yearString, NumberStyles.None, CultureInfo.InvariantCulture, out int years))
                throw new FormatException($"Cannot parse year count '{yearString}' in tenor '{timeToExpiry}'.");
            spotDate = getForwardDate(horizonDate, _tAdd);
            deliveryDate = addYears(spotDate, years);
            expiryDate = getBackwardDate(deliveryDate, _tAdd);
        }

        // explicit ISO date
        else
        {
            if (!DateTime.TryParseExact(timeToExpiry, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime explicitDate))
                throw new FormatException($"Cannot parse '{timeToExpiry}' as an explicit date. Expected format: yyyy-MM-dd.");

            spotDate = getForwardDate(horizonDate, _tAdd);
            deliveryDate = getForwardDate(explicitDate, _tAdd);
            expiryDate = getBackwardDate(deliveryDate, _tAdd);
        }

        int days = (int)Math.Round((expiryDate - horizonDate).TotalDays, MidpointRounding.AwayFromZero);

        return new Convention
        {
            SpotDate = spotDate,
            ExpiryDate = expiryDate,
            DeliveryDate = deliveryDate,
            Days = days
        };
    }

    // Finding Country Names for the CCY
    internal static string[] ctryNames(string CCY)
    {
        string ccyBase;
        string ccyPrice;

        if (CCY.Contains("/"))
        {
            ccyBase = CCY.Substring(0, 3);
            ccyPrice = CCY.Substring(4, 3);
        }
        else
        {
            ccyBase = CCY.Substring(0, 3);
            ccyPrice = CCY.Substring(3, 3);
        }

        // Default to empty string so Calendar never receives a null entry
        // for currencies that are not in the lookup dictionary.
        CurrencyToCalendar.TryGetValue(ccyBase, out var calBase);
        CurrencyToCalendar.TryGetValue(ccyPrice, out var calPrice);

        return [calBase ?? string.Empty, calPrice ?? string.Empty];
    }

    // Find CCY Holiday
    private bool isCCYHoliday(DateTime date)
    {
        bool output = false;

        if (this._calendar.IsHoliday(date) == true)
        {
            output = true;
        }

        return output;
    }

    // if CCY is USD check for other leg holiday
    // Bug #4 fix: use the cached _nonUsCalendar instead of allocating a new Calendar per call.
    private bool isCCYHoliday_notUS(DateTime date)
    {
        return _nonUsCalendar.IsHoliday(date);
    }

    // Check if it is CCy holiday OR US holiday
    // Bug #3 fix: use the cached _usaCalendar instead of allocating a new Calendar per call.
    private bool isCCYorUSHoliday(DateTime date)
    {
        return _calendar.IsHoliday(date) || _usaCalendar.IsHoliday(date);
    }

    //Check if it is First of jan
    private bool isFirstOfJan(DateTime date)
    {
        bool output = false;

        if (date.Month == 1 && date.Day == 1)
        {
            output = true;
        }

        return output;
    }

    // Check if date is end of month
    private bool isEndOfMonth(DateTime date)
    {
        bool output = false;

        if (date.Day.ToString() == DateTime.DaysInMonth(date.Year, date.Month).ToString())
        {
            output = true;
        }

        return output;
    }

    //Check if it is date is end of month date
    private bool isLastBusDayOfMonth(DateTime date)
    {
        bool output = false;

        if (isEndOfMonth(date) == true)
        {
            output = true;
        }
        else if (date.DayOfWeek == DayOfWeek.Friday)
        {
            if (isEndOfMonth(date.AddDays(1)) == true || isEndOfMonth(date.AddDays(2)) == true)
            {
                output = true;
            }
        }

        return output;
    }

    // Move date bus day back or forward
    // Bug #19 fix: replaced the arithmetic shortcut with an iterative walk that
    // also skips CCY and US holidays, matching the contract assumed by all callers.
    private DateTime moveBusinessDays(DateTime startDate, int businessDays)
    {
        if (businessDays == 0)
            return startDate;

        int direction = Math.Sign(businessDays);
        int remaining = Math.Abs(businessDays);
        DateTime current = startDate;
        int guard = 0;

        while (remaining > 0)
        {
            if (guard++ > MaxRollDays * remaining)
                throw new InvalidOperationException(
                    $"moveBusinessDays exceeded safety limit from {startDate:yyyy-MM-dd} for {_ccy}. Holiday data may be corrupt or missing.");

            current = current.AddDays(direction);

            if (current.DayOfWeek != DayOfWeek.Saturday
                && current.DayOfWeek != DayOfWeek.Sunday
                && !isCCYorUSHoliday(current))
            {
                remaining--;
            }
        }

        return current;
    }

    /// <summary>
    /// Steps backward N non-weekend days from <paramref name="startDate"/>.
    /// Unlike <see cref="moveBusinessDays"/>, this method does NOT skip CCY or
    /// US holidays — expiry dates may fall on public holidays (only 01-Jan is
    /// excluded, handled separately in <see cref="getBackwardDate"/>).
    /// </summary>
    private static DateTime stepBackNonWeekendDays(DateTime startDate, int days)
    {
        int remaining = days;
        DateTime current = startDate;

        while (remaining > 0)
        {
            current = current.AddDays(-1);
            if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
                remaining--;
        }

        return current;
    }

    //get delivery date if we add months
    // Bug #5 fix: after holiday-rolling in the EOM branch, verify the date hasn't
    // crossed into the next month. If it has, roll backward instead (Modified Following).
    private DateTime addMonths(DateTime spotDate, int nrMonthAdd)
    {
        DateTime output;
        output = spotDate.AddMonths(nrMonthAdd);

        if (isLastBusDayOfMonth(spotDate) == true)
        {
            int targetMonth = output.Month;

            int rollDays = 0;
            do
            {
                if (rollDays++ > MaxRollDays)
                    throw new InvalidOperationException(
                        $"addMonths EOM roll exceeded {MaxRollDays} days from {output:yyyy-MM-dd} for {_ccy}. Holiday data may be corrupt or missing.");

                if (isLastBusDayOfMonth(output) == false)
                {
                    output = output.AddDays(1);
                }
            } while (isLastBusDayOfMonth(output) == false);

            if (output.DayOfWeek == DayOfWeek.Saturday)
            {
                output = output.AddDays(-1);
            }
            else if (output.DayOfWeek == DayOfWeek.Sunday)
            {
                output = output.AddDays(-2);
            }

            int holidayRoll = 0;
            while (isCCYorUSHoliday(output) == true)
            {
                if (holidayRoll++ > MaxRollDays)
                    throw new InvalidOperationException(
                        $"addMonths holiday skip exceeded {MaxRollDays} days from {output:yyyy-MM-dd} for {_ccy}. Holiday data may be corrupt or missing.");

                output = output.AddDays(1);
            }

            // Bug #5 fix: if holiday rolling pushed us into the next month,
            // fall back to the last good business day in the target month
            // (Modified Following convention for EOM dates).
            if (output.Month != targetMonth)
            {
                // Start from the last calendar day of the target month and walk backward
                output = new DateTime(output.Year, targetMonth, DateTime.DaysInMonth(output.Year, targetMonth));

                // Skip weekends
                while (output.DayOfWeek == DayOfWeek.Saturday || output.DayOfWeek == DayOfWeek.Sunday)
                {
                    output = output.AddDays(-1);
                }

                // Skip holidays going backward
                int backRoll = 0;
                while (isCCYorUSHoliday(output) == true)
                {
                    if (backRoll++ > MaxRollDays)
                        throw new InvalidOperationException(
                            $"addMonths EOM backward roll exceeded {MaxRollDays} days from {output:yyyy-MM-dd} for {_ccy}. Holiday data may be corrupt or missing.");

                    output = output.AddDays(-1);

                    // Also skip weekends when rolling backward
                    while (output.DayOfWeek == DayOfWeek.Saturday || output.DayOfWeek == DayOfWeek.Sunday)
                    {
                        output = output.AddDays(-1);
                    }
                }
            }
        }
        else
        {
            if (output.DayOfWeek == DayOfWeek.Saturday)
            {
                output = output.AddDays(2);
            }
            else if (output.DayOfWeek == DayOfWeek.Sunday)
            {
                output = output.AddDays(1);
            }

            int holidayRoll = 0;
            while (isCCYorUSHoliday(output) == true)
            {
                if (holidayRoll++ > MaxRollDays)
                    throw new InvalidOperationException(
                        $"addMonths holiday skip exceeded {MaxRollDays} days from {output:yyyy-MM-dd} for {_ccy}. Holiday data may be corrupt or missing.");

                output = output.AddDays(1);
            }
        }

        return output;
    }

    // Get delivery dates if we add years
    private DateTime addYears(DateTime spotDate, int nrYearsAdd)
    {
        DateTime output;
        output = spotDate;

        output = spotDate.AddYears(nrYearsAdd);

        if (output.DayOfWeek == DayOfWeek.Saturday)
        {
            output = output.AddDays(2);
        }
        else if (output.DayOfWeek == DayOfWeek.Sunday)
        {
            output = output.AddDays(1);
        }

        int holidayRoll = 0;
        while (isCCYorUSHoliday(output) == true)
        {
            if (holidayRoll++ > MaxRollDays)
                throw new InvalidOperationException(
                    $"addYears holiday skip exceeded {MaxRollDays} days from {output:yyyy-MM-dd} for {_ccy}. Holiday data may be corrupt or missing.");

            output = output.AddDays(1);
        }

        return output;
    }


    // Move forward function (Spot date)
    internal DateTime getForwardDate(DateTime horizonDate, int tAdd)
    {
        DateTime output = horizonDate.AddDays(1);

        int numBusDays = 0;
        if (this._ccy.Substring(0, 3) != "USD" && this._ccy.Substring(3, 3) != "USD")
        {
            int roll = 0;
            do
            {
                if (roll++ > MaxRollDays)
                    throw new InvalidOperationException(
                        $"getForwardDate (non-USD) exceeded {MaxRollDays} days from {horizonDate:yyyy-MM-dd} for {_ccy}. Holiday data may be corrupt or missing.");

                if (isCCYHoliday(output) == false && output.DayOfWeek != DayOfWeek.Saturday && output.DayOfWeek != DayOfWeek.Sunday)
                {
                    numBusDays = numBusDays + 1;
                }
                if (numBusDays < tAdd)
                {
                    output = output.AddDays(1);
                }

            } while (numBusDays < tAdd);
        }
        else
        {
            int roll = 0;
            do
            {
                if (roll++ > MaxRollDays)
                    throw new InvalidOperationException(
                        $"getForwardDate (USD) exceeded {MaxRollDays} days from {horizonDate:yyyy-MM-dd} for {_ccy}. Holiday data may be corrupt or missing.");

                if (isCCYHoliday_notUS(output) == false && output.DayOfWeek != DayOfWeek.Saturday && output.DayOfWeek != DayOfWeek.Sunday)
                {
                    numBusDays = numBusDays + 1;
                }
                if (numBusDays < tAdd)
                {
                    output = output.AddDays(1);
                }

            } while (numBusDays < tAdd);
        }

        // Check if Spot date is US holiday, if TRUE then move FORWARD 1 day until we find a non-US and non-CCY holiday
        // Bug #3 fix: use cached _usaCalendar instead of new Calendar("USA", _holidays)
        int usRoll = 0;
        do
        {
            if (usRoll++ > MaxRollDays)
                throw new InvalidOperationException(
                    $"getForwardDate US-holiday skip exceeded {MaxRollDays} days from {horizonDate:yyyy-MM-dd} for {_ccy}. Holiday data may be corrupt or missing.");

            if (_usaCalendar.IsHoliday(output) == true)
            {
                output = output.AddDays(1);
            }
        } while (isCCYorUSHoliday(output) == true);

        return output;
    }

    // Move Backwards function (from Delivery to Expiry date)
    private DateTime getBackwardDate(DateTime deliveryDate, int tAdd)
    {
        // Step back tAdd non-weekend days from delivery — holidays are allowed on expiry.
        DateTime output = stepBackNonWeekendDays(deliveryDate, tAdd);

        // 01-Jan is the only date that cannot be an expiry — roll back one calendar day.
        if (isFirstOfJan(output))
            output = output.AddDays(-1);

        // Ensure the delivery date still has at least one valid settlement day
        // (non-weekend, non-CCY-non-US-holiday) between expiry and delivery.
        bool businessDayFound = false;
        int roll = 0;
        do
        {
            if (roll++ > MaxRollDays)
                throw new InvalidOperationException(
                    $"getBackwardDate exceeded {MaxRollDays} iterations from {deliveryDate:yyyy-MM-dd} for {_ccy}. Holiday data may be corrupt or missing.");

            int timespan = (int)(deliveryDate - output).TotalDays;
            for (int i = 1; i < timespan; i++)
            {
                DateTime dayToCheck = output.AddDays(i);
                if (dayToCheck.DayOfWeek != DayOfWeek.Sunday
                    && dayToCheck.DayOfWeek != DayOfWeek.Saturday
                    && !isCCYHoliday_notUS(dayToCheck))
                {
                    businessDayFound = true;
                }
            }

            if (!businessDayFound)
            {
                // Still no valid settlement gap — step back one more non-weekend day.
                output = stepBackNonWeekendDays(output, 1);

                if (isFirstOfJan(output))
                    output = output.AddDays(-1);
            }

        } while (!businessDayFound);

        return output;
    }
}

/// <summary>
/// Tiny helper so <see cref="DateConvention.BuildNonUsCalendar"/> can resolve
/// country names without requiring a fully constructed <see cref="DateConvention"/>.
/// </summary>
internal sealed class DateConventionCountryResolver
{
    private static readonly Dictionary<string, string> CurrencyToCalendar =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["EUR"] = "TARGET",
            ["USD"] = "USA",
            ["SEK"] = "SWEDEN",
            ["NOK"] = "NORWAY",
            ["GBP"] = "ENGLAND",
            ["CAD"] = "CANADA",
            ["CHF"] = "SWITZERLAND",
            ["AUD"] = "AUSTRALIA",
            ["RUB"] = "RUSSIA",
            ["JPY"] = "JAPAN",
        };

    public string[] Resolve(string ccy)
    {
        string ccyBase;
        string ccyPrice;

        if (ccy.Contains("/"))
        {
            ccyBase = ccy.Substring(0, 3);
            ccyPrice = ccy.Substring(4, 3);
        }
        else
        {
            ccyBase = ccy.Substring(0, 3);
            ccyPrice = ccy.Substring(3, 3);
        }

        CurrencyToCalendar.TryGetValue(ccyBase, out var calBase);
        CurrencyToCalendar.TryGetValue(ccyPrice, out var calPrice);

        return [calBase ?? string.Empty, calPrice ?? string.Empty];
    }
}
