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
#endregion

namespace FxTradeConfirmation.Helpers
{
    class Calendar
    {
        #region Variables

        private DataTable _holidays = new DataTable();
        private string[] _country;
        public DataTable dt = new DataTable();

        // Below you see the countries for which the holiday calendar is covering, TARGET is the offical EURO calendar
        //private string[] _countries = new string[] { "AUSTRALIA", "CANADA", "CHINA", "DENMARK", "ENGLAND", "EURO", "FRANCE", "GERMANY", "GLOBAL", "ITALY", "JAPAN", "NORWAY", "SWEDEN", "SWITZERLAND", "TARGET", "USA" };

        #endregion

        #region Constructor
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
        #endregion

        #region Properties
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

        #endregion

        #region Methods
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
        #endregion

        #region Functions
        public static bool IsWeekend(DateTime date)
        {
            return new[] { DayOfWeek.Sunday, DayOfWeek.Saturday }.Contains(date.DayOfWeek);
        }
        #endregion

    }
}
