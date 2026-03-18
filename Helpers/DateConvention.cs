#region Namespaces
using System;
using System.IO;
using System.Collections.Generic;
using System.Collections;
using System.ComponentModel;
using System.Data;
using System.Data.Sql;
using System.Data.SqlClient;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Diagnostics;
using System.Threading;
using System.Globalization;
#endregion

namespace FxTradeConfirmation.Helpers
{
    public class Convention
    {
        #region Properties
        public DateTime SpotDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public DateTime DeliveryDate { get; set; }
        public int Days { get; set; }
        #endregion
    }

    public class DateConvention
    {
        #region Variables
        private string[] ctryCurrency = new string[] { "EUR", "USD", "SEK", "NOK", "GBP", "CAD", "CHF", "AUD", "RUB", "JPY" };
        private string[] ctryCalender = new string[] { "TARGET", "USA", "SWEDEN", "NORWAY", "ENGLAND", "CANADA", "SWITZERLAND", "AUSTRALIA", "RUSSIA", "JAPAN" };

        private DataTable _holidays = new DataTable();
        private string _ccy;
        private int _tAdd;

        private DateTime _spotDate;
        private DateTime _expiryDate;
        private DateTime _deliveryDate;
        private int _days;

        private Calendar _calendar;
        #endregion

        #region Constructor
        public DateConvention(string CCY, DataTable Holidays)
        {
            _holidays = Holidays;
            _ccy = CCY;

            if (CCY.Replace("/", "") == "USDCAD" || CCY == "USDTRY" || CCY == "USDPHP" || CCY == "USDRUB" || CCY == "USDKZT" || CCY == "USDPKR")
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
        #endregion

        #region Property
        public string SpotDate
        {
            get
            {
                return this._spotDate.ToString("yyyy-MM-dd");
            }
        }
        public string ExpiryDate
        {
            get
            {
                return this._expiryDate.ToString("yyyy-MM-dd");
            }
        }
        public string DeliveryDate
        {
            get
            {
                return this._deliveryDate.ToString("yyyy-MM-dd");
            }
        }
        public int Days
        {
            get
            {
                return this._days;
            }
        }
        #endregion

        #region Method
        public Convention GetConvention(string timeToExpiry)
        {
            DateTime horizonDate = DateTime.Parse(DateTime.Now.ToShortDateString());

            // Over-night
            if (timeToExpiry.ToLower() == "on")
            {
                _spotDate = getForwardDate(horizonDate, _tAdd);
                _expiryDate = moveBusinessDays(horizonDate, 1);

                if (isFirstOfJan(_expiryDate) == true)
                {
                    _expiryDate = moveBusinessDays(_expiryDate, 1);
                }

                _deliveryDate = getForwardDate(_expiryDate, _tAdd);
            }

            //days
            else if (timeToExpiry.ToLower().EndsWith("d"))
            {
                _spotDate = getForwardDate(horizonDate, _tAdd);
                double daysToExpiry = double.Parse(timeToExpiry.ToLower().Replace("d", ""));
                _expiryDate = horizonDate.AddDays(daysToExpiry);

                if (isFirstOfJan(_expiryDate) == true)
                {
                    _expiryDate = moveBusinessDays(_expiryDate, 1);
                }
                _deliveryDate = getForwardDate(_expiryDate, _tAdd);
            }
            //weeks
            else if (timeToExpiry.ToLower().EndsWith("w"))
            {
                _spotDate = getForwardDate(horizonDate, _tAdd);

                timeToExpiry = timeToExpiry.ToLower().Replace("w", "");
                _expiryDate = horizonDate.AddDays(7 * int.Parse(timeToExpiry));

                if (isFirstOfJan(_expiryDate) == true)
                {
                    _expiryDate = moveBusinessDays(_expiryDate, 1);
                }
                _deliveryDate = getForwardDate(_expiryDate, _tAdd);
            }
            //months
            else if (timeToExpiry.ToLower().EndsWith("m"))
            {
                string monthString = timeToExpiry.Substring(0, timeToExpiry.Length - 1);
                if (int.TryParse(monthString, out int months))
                {
                    _spotDate = getForwardDate(horizonDate, _tAdd);
                    _deliveryDate = addMonths(_spotDate, months);
                    _expiryDate = getBackwardDate(_deliveryDate, _tAdd);
                }
            }
            //years
            else if (timeToExpiry.ToLower().EndsWith("y"))
            {
                string yearString = timeToExpiry.Substring(0, timeToExpiry.Length - 1);
                if (int.TryParse(yearString, out int years))
                {
                    _spotDate = getForwardDate(horizonDate, _tAdd);
                    _deliveryDate = addYears(_spotDate, years);
                    _expiryDate = getBackwardDate(_deliveryDate, _tAdd);
                }
            }
            else
            {
                _spotDate = getForwardDate(horizonDate, _tAdd);
                _deliveryDate = getForwardDate(DateTime.Parse(timeToExpiry), _tAdd);
                _expiryDate = getBackwardDate(_deliveryDate, _tAdd);
            }

            _days = int.Parse((_expiryDate - horizonDate).TotalDays.ToString());

            var result = new Convention() { SpotDate = _spotDate, ExpiryDate = _expiryDate, DeliveryDate = _deliveryDate, Days = _days };

            return result;
        }
        #endregion

        #region Functions
        // Finding Country Names for the CCY
        public string[] ctryNames(string CCY)
        {
            string[] output = new string[2];


            string ccyBase = "";
            string ccyPrice = "";
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

            int cols = this.ctryCurrency.Length;

            for (int i = 0; i < cols; i++)
            {
                if (this.ctryCurrency[i] == ccyBase)
                {
                    output[0] = this.ctryCalender[i];
                }
                else if (this.ctryCurrency[i] == ccyPrice)
                {
                    output[1] = this.ctryCalender[i];
                }
            }
            return output;
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
                do
                {
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
                while (isCCYorUSHoliday(output) == true)
                {
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
                while (isCCYorUSHoliday(output) == true)
                {
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
            while (isCCYorUSHoliday(output) == true)
            {
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
                do
                {
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
                do
                {
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

            // Check if Spot date is US holiday, if TRUE them move FORWARD 1 day until we find a non-US and non-CCY holiday
            do
            {
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
            do
            {
                int timespan = int.Parse((deliveryDate - output).TotalDays.ToString());
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
        #endregion
    }




}