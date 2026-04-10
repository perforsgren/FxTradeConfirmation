using System.Data;
using System.Collections.Generic;
using System.Linq;

namespace FxTradeConfirmation.Helpers
{

    class Calendar
    {
        private DataTable _holidays = new DataTable();
        private string[] _country;

        public Calendar(DataTable Holidays)
        {
            _holidays.Columns.Add("Market", typeof(string));
            _holidays.Columns.Add("HolidayDate", typeof(DateTime));
            _holidays = Holidays;
        }

        public Calendar(string Country, DataTable Holidays)
        {
            _country = new string[] { Country };
            _holidays.Columns.Add("Market", typeof(string));
            _holidays.Columns.Add("HolidayDate", typeof(DateTime));

            foreach (DataRow row in Holidays.Rows)
            {
                if (row["Market"].ToString() == Country)
                {
                    _holidays.Rows.Add(row.ItemArray);
                }
            }
        }

        public Calendar(string[] Country, DataTable Holidays)
        {
            _country = Country;
            _holidays.Columns.Add("Market", typeof(string));
            _holidays.Columns.Add("HolidayDate", typeof(DateTime));

            foreach (DataRow row in Holidays.Rows)
            {
                for (int i = 0; i < Country.Length; i++)
                {
                    if (row["Market"].ToString() == Country[i])
                    {
                        _holidays.Rows.Add(row.ItemArray);
                    }
                }
            }
        }

        public string[] Country
        {
            get
            {
                return this._country;
            }
        }

        public DataTable Holidays
        {
            get
            {
                return this._holidays;
            }
        }

        public bool IsHoliday(DateTime date)
        {
            if (IsWeekend(date))
                return true;

            var dateOnly = date.Date;

            foreach (DataRow row in _holidays.Rows)
            {
                if (row["HolidayDate"] is DateTime holidayDate && holidayDate.Date == dateOnly)
                    return true;
            }

            return false;
        }

        public static bool IsWeekend(DateTime date)
        {
            return new[] { DayOfWeek.Sunday, DayOfWeek.Saturday }.Contains(date.DayOfWeek);
        }

        /// <summary>
        /// Checks whether <paramref name="date"/> is a local market holiday
        /// (excluding weekends) for any of the given <paramref name="markets"/>.
        /// Returns the match status and a human-readable description of the
        /// matching holiday(s).
        /// </summary>
        /// <param name="date">The date to check.</param>
        /// <param name="holidays">
        /// The full holidays DataTable (columns: Market, HolidayDate, and
        /// optionally HolidayName).
        /// </param>
        /// <param name="markets">
        /// Market/calendar names to check (e.g. "SWEDEN", "TARGET").
        /// </param>
        /// <returns>
        /// A tuple where <c>isHoliday</c> is true when at least one market
        /// has a holiday on that date, and <c>description</c> contains a
        /// comma-separated list of matching market names (with holiday name
        /// if available).
        /// </returns>
        public static (bool isHoliday, string description) IsMarketHoliday(
            DateTime date, DataTable holidays, IEnumerable<string> markets)
        {
            if (holidays == null || holidays.Rows.Count == 0)
                return (false, string.Empty);

            var dateOnly = date.Date;
            var marketSet = new HashSet<string>(markets, StringComparer.OrdinalIgnoreCase);
            bool hasNameColumn = holidays.Columns.Contains("HolidayName");

            var matches = new List<string>();

            foreach (DataRow row in holidays.Rows)
            {
                if (row["HolidayDate"] is DateTime holidayDate
                    && holidayDate.Date == dateOnly
                    && row["Market"] is string market
                    && marketSet.Contains(market))
                {
                    string name = hasNameColumn && row["HolidayName"] is string hn && !string.IsNullOrWhiteSpace(hn)
                        ? $"{market} ({hn})"
                        : market;
                    matches.Add(name);
                }
            }

            return matches.Count > 0
                ? (true, string.Join(", ", matches))
                : (false, string.Empty);
        }
    }
}
