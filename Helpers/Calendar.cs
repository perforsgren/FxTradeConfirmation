using System.Data;

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
            bool output = false;

            foreach (DataRow row in _holidays.Rows)
            {
                if (row["HolidayDate"].ToString() == date.ToString() || IsWeekend(date) == true)
                {
                    output = true;
                    break;
                }
            }

            return output;
        }

        public static bool IsWeekend(DateTime date)
        {
            return new[] { DayOfWeek.Sunday, DayOfWeek.Saturday }.Contains(date.DayOfWeek);
        }
    }
}
