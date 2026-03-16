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
using System.Data.Odbc;
#endregion

namespace FxTradeConfirmation.Helpers
{
    class HolidayDAO
    {
        #region Variables
        private DataTable _dbDataTable = new DataTable();
        private string[] _country;
        public DataTable Holidays = new DataTable();
        #endregion

        #region Constructor
        public HolidayDAO()
        {
            Holidays.Columns.Add("Market", typeof(string));
            Holidays.Columns.Add("HolidayDate", typeof(DateTime));

            Holidays = _dbDataTable;
        }

        public HolidayDAO(string Country)
        {
            Holidays.Columns.Add("Market", typeof(string));
            Holidays.Columns.Add("HolidayDate", typeof(DateTime));
            _country = new string[] { Country };

            foreach (DataRow row in _dbDataTable.Rows)
            {
                if (row["Market"].ToString() == Country)
                {
                    Holidays.Rows.Add(row.ItemArray);
                }
            }
        }

        public HolidayDAO(string[] Country)
        {
            Holidays.Columns.Add("Market", typeof(string));
            Holidays.Columns.Add("HolidayDate", typeof(DateTime));
            _country = Country;

            foreach (DataRow row in _dbDataTable.Rows)
            {
                for (int i = 0; i < Country.Length; i++)
                {
                    if (row["Market"].ToString() == Country[i])
                    {
                        Holidays.Rows.Add(row.ItemArray);
                    }
                }
            }
        }
        #endregion

        #region Method
        public DataTable RetrieveData()
        {
            Exception cannotConnectException = null;
            int attempts = 5;
            while (attempts > 0)
            {
                try
                {
                    SqlConnection myCon = new SqlConnection("Data Source=AHSKvant-prod-db;Initial Catalog=AHS;Integrated Security=True;Connect Timeout = 15");
                    myCon.Open();
                    SqlCommand myCommand = new SqlCommand();
                    myCommand.CommandText = "SELECT MARKET, HOLIDAY_DATE from Holiday WHERE HOLIDAY_DATE>=" + "'" + DateTime.Now + "'" + " AND HOLIDAY_DATE<=" + "'" + DateTime.Now.AddYears(7) + "'";
                    myCommand.Connection = myCon;
                    myCommand.CommandTimeout = 15;
                    SqlDataAdapter myAdapter = new SqlDataAdapter(myCommand);
                    SqlCommandBuilder cb = new SqlCommandBuilder(myAdapter);
                    myAdapter.Fill(_dbDataTable);
                    attempts = 0;
                }
                catch (SqlException exception)
                {
                    cannotConnectException = exception;
                    System.Threading.Thread.Sleep(100);
                    attempts--;
                    //throw new InvalidOperationException("No connection to database (get last update time).", e);
                    //MessageBox.Show(exception.ToString());
                }
            }
            return _dbDataTable;
        }
        #endregion

    }
}
