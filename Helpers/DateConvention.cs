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
    private string[] ctryCurrency = new string[] { "EUR", "USD", "SEK", "NOK", "GBP", "CAD", "CHF", "AUD", "RUB", "JPY" };
    private string[] ctryCalender = new string[] { "TARGET", "USA", "SWEDEN", "NORWAY", "ENGLAND", "CANADA", "SWITZERLAND", "AUSTRALIA", "RUSSIA", "JAPAN" };

    private DataTable _holidays = new DataTable();
    private string _ccy;
    private int _tAdd;

    // NOTE: _spotDate, _expiryDate, _deliveryDate, _days removed —
    // they were written and read only within GetConvention, making
    // them de-facto local variables stored as fields. Concurrent calls
    // on a shared instance would corrupt each other's intermediate state.

    private Calendar _calendar;

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
    public string[] ctryNames(string CCY)
    {
        string ccyBase;
        string ccyPrice;

        if (CCY.Contains("/"))
        {
            ccyBase  = CCY.Substring(0, 3);
            ccyPrice = CCY.Substring(4, 3);
        }
        else
        {
            ccyBase  = CCY.Substring(0, 3);
            ccyPrice = CCY.Substring(3, 3);
        }

        // Default to empty string so Calendar never receives a null entry
        // for currencies that are not in the hardcoded lookup table.
        string calBase  = string.Empty;
        string calPrice = string.Empty;

        for (int i = 0; i < ctryCurrency.Length; i++)
        {
            if (ctryCurrency[i] == ccyBase)
                calBase = ctryCalender[i];
            else if (ctryCurrency[i] == ccyPrice)
                calPrice = ctryCalender[i];
        }

        return [calBase, calPrice];
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
    private bool isCCYHoliday_notUS(DateTime date)
    {
        bool output = false;

        string[] ctryCal;
        ctryCal = ctryNames(this._ccy);

        string ctry1 = string.Empty;
        string ctry2 = string.Empty;

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
        else if (ctryCal[0] != "USA" || ctryCal[1] != "USA")
        {
            ctry1 = ctryCal[0];
            ctry2 = ctryCal[1];
        }

        Calendar cal = new Calendar(new string[] { ctry1, ctry2 }, _holidays);

        if (cal.IsHoliday(date) == true)
        {
            output = true;
        }

        return output;
    }

    // Check if it is CCy holiday OR US holiday
    private bool isCCYorUSHoliday(DateTime date)
    {
        bool output = false;

        if (this._calendar.IsHoliday(date) == true || new Calendar("USA", _holidays).IsHoliday(date) == true)
        {
            output = true;
        }

        return output;
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
    private DateTime moveBusinessDays(DateTime startDate, int businessDays)
    {
        int direction = Math.Sign(businessDays);
        if (direction == 1)
        {
            if (startDate.DayOfWeek == DayOfWeek.Saturday)
            {
                startDate = startDate.AddDays(2);
                businessDays = businessDays - 1;
            }
            else if (startDate.DayOfWeek == DayOfWeek.Sunday)
            {
                startDate = startDate.AddDays(1);
                businessDays = businessDays - 1;
            }
        }
        else
        {
            if (startDate.DayOfWeek == DayOfWeek.Saturday)
            {
                startDate = startDate.AddDays(-1);
                businessDays = businessDays + 1;
            }
            else if (startDate.DayOfWeek == DayOfWeek.Sunday)
            {
                startDate = startDate.AddDays(-2);
                businessDays = businessDays + 1;
            }
        }

        int initialDayOfWeek = Convert.ToInt32(startDate.DayOfWeek);

        int weeksBase = Math.Abs(businessDays / 5);
        int addDays = Math.Abs(businessDays % 5);

        if ((direction == 1 && addDays + initialDayOfWeek > 5) ||
             (direction == -1 && addDays >= initialDayOfWeek))
        {
            addDays += 2;
        }

        int totalDays = (weeksBase * 7) + addDays;

        return startDate.AddDays(totalDays * direction);
    }

    //get delivery date if we add months
    private DateTime addMonths(DateTime spotDate, int nrMonthAdd)
    {
        DateTime output;
        output = spotDate.AddMonths(nrMonthAdd);

        if (isLastBusDayOfMonth(spotDate) == true)
        {
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
    public DateTime getForwardDate(DateTime horizonDate, int tAdd)
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
        int usRoll = 0;
        do
        {
            if (usRoll++ > MaxRollDays)
                throw new InvalidOperationException(
                    $"getForwardDate US-holiday skip exceeded {MaxRollDays} days from {horizonDate:yyyy-MM-dd} for {_ccy}. Holiday data may be corrupt or missing.");

            if (new Calendar("USA", _holidays).IsHoliday(output) == true)
            {
                output = output.AddDays(1);
            }
        } while (isCCYorUSHoliday(output) == true);

        return output;
    }

    // Move Backwards function (from Delivery to Expiry date)
    private DateTime getBackwardDate(DateTime deliveryDate, int tAdd)
    {
        DateTime output;
        output = deliveryDate;

        output = moveBusinessDays(deliveryDate, -tAdd);

        if (output.DayOfWeek == DayOfWeek.Saturday)
        {
            output = output.AddDays(-1);
        }
        else if (output.DayOfWeek == DayOfWeek.Sunday)
        {
            output = output.AddDays(-2);
        }

        if (isFirstOfJan(output) == true)
        {
            output = output.AddDays(-1);
        }

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
                if (dayToCheck.DayOfWeek != DayOfWeek.Sunday && dayToCheck.DayOfWeek != DayOfWeek.Saturday && isCCYHoliday_notUS(dayToCheck) == false)
                {
                    businessDayFound = true;
                }
            }
            if (businessDayFound == false)
            {
                output = moveBusinessDays(output, -1);

                if (output.DayOfWeek == DayOfWeek.Saturday)
                {
                    output = output.AddDays(-1);
                }
                else if (output.DayOfWeek == DayOfWeek.Sunday)
                {
                    output = output.AddDays(-2);
                }
            }

        } while (businessDayFound == false);

        return output;
    }
}