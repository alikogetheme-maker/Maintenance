using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using SAPbobsCOM;

namespace MaintenanceAddon.Core
{
    /// <summary>Ligne de résultat d'une requête, avec conversions tolérantes.</summary>
    internal sealed class Row
    {
        private readonly Dictionary<string, object> _values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public object this[string column]
        {
            get { return _values.TryGetValue(column, out object v) ? v : null; }
            set { _values[column] = value; }
        }

        public string Str(string column)
        {
            object v = this[column];
            return v == null || v is DBNull ? "" : Convert.ToString(v, CultureInfo.InvariantCulture).Trim();
        }

        public double Dbl(string column)
        {
            object v = this[column];
            if (v == null || v is DBNull)
                return 0;
            if (v is string s)
                return Sql.ParseDouble(s);
            return Convert.ToDouble(v, CultureInfo.InvariantCulture);
        }

        public int Int(string column)
        {
            return (int)Math.Round(Dbl(column));
        }

        /// <summary>Date ou null (le DI API renvoie 30/12/1899 pour une date vide).</summary>
        public DateTime? Date(string column)
        {
            object v = this[column];
            if (v is DateTime d)
                return d.Year < 1901 ? (DateTime?)null : d.Date;
            if (v is string s && s.Length >= 8 && DateTime.TryParseExact(s.Substring(0, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime p))
                return p;
            return null;
        }
    }

    /// <summary>Accès SQL en lecture via le Recordset DI API (SQL Server).</summary>
    internal static class Sql
    {
        /// <summary>Littéral texte SQL échappé.</summary>
        public static string Q(string value)
        {
            return "N'" + (value ?? "").Replace("'", "''") + "'";
        }

        public static string D(DateTime date)
        {
            return "'" + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "'";
        }

        public static string N(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        public static List<Row> Rows(string sql)
        {
            var rows = new List<Row>();
            Recordset rs = (Recordset)DiCompany.Instance.GetBusinessObject(BoObjectTypes.BoRecordset);
            try
            {
                rs.DoQuery(sql);
                int count = rs.Fields.Count;
                var names = new string[count];
                for (int i = 0; i < count; i++)
                    names[i] = rs.Fields.Item(i).Name;

                while (!rs.EoF)
                {
                    var row = new Row();
                    for (int i = 0; i < count; i++)
                        row[names[i]] = rs.Fields.Item(i).Value;
                    rows.Add(row);
                    rs.MoveNext();
                }
            }
            finally
            {
                Marshal.ReleaseComObject(rs);
            }
            return rows;
        }

        public static Row First(string sql)
        {
            List<Row> rows = Rows(sql);
            return rows.Count == 0 ? null : rows[0];
        }

        public static object Scalar(string sql)
        {
            Recordset rs = (Recordset)DiCompany.Instance.GetBusinessObject(BoObjectTypes.BoRecordset);
            try
            {
                rs.DoQuery(sql);
                return rs.EoF ? null : rs.Fields.Item(0).Value;
            }
            finally
            {
                Marshal.ReleaseComObject(rs);
            }
        }

        public static string ScalarStr(string sql)
        {
            object v = Scalar(sql);
            return v == null || v is DBNull ? "" : Convert.ToString(v, CultureInfo.InvariantCulture).Trim();
        }

        public static double ScalarDbl(string sql)
        {
            object v = Scalar(sql);
            if (v == null || v is DBNull)
                return 0;
            return v is string s ? ParseDouble(s) : Convert.ToDouble(v, CultureInfo.InvariantCulture);
        }

        public static bool Exists(string sql)
        {
            return Scalar(sql) != null;
        }

        /// <summary>Nombre saisi ou lu dans une source de données UI (point ou virgule décimale).</summary>
        public static double ParseDouble(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;
            string t = text.Trim().Replace(" ", "").Replace(" ", "").Replace(" ", "");
            if (t.Contains(",") && !t.Contains("."))
                t = t.Replace(",", ".");
            else if (t.Contains(",") && t.Contains("."))
                t = t.Replace(",", "");
            return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
        }

        /// <summary>Date au format "yyyyMMdd" (sources de données UI) ou null.</summary>
        public static DateTime? ParseDate(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            string t = text.Trim();
            if (t.Length >= 8 && DateTime.TryParseExact(t.Substring(0, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d))
                return d;
            return null;
        }

        /// <summary>Prochain code numérique libre d'une table simple (Code = Name).</summary>
        public static string NextCode(string table)
        {
            double max = ScalarDbl("SELECT MAX(CAST(\"Code\" AS BIGINT)) FROM " + Db.T(table) + " WHERE ISNUMERIC(\"Code\") = 1");
            return ((long)max + 1).ToString("D10", CultureInfo.InvariantCulture);
        }

        /// <summary>Montant au format de la société (séparateurs fr-FR).</summary>
        public static string Amount(double value)
        {
            return value.ToString("N2", CultureInfo.GetCultureInfo("fr-FR"));
        }
    }
}
