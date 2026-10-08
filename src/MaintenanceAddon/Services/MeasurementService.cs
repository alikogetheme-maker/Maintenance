using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using SAPbobsCOM;
using MaintenanceAddon.Core;

namespace MaintenanceAddon.Services
{
    internal sealed class MeasuringPoint
    {
        public string Point;
        public string Descr;
        public string Unit;
        public bool IsCounter;
        public double Min;
        public double Max;
    }

    internal sealed class MeasurementResult
    {
        public double Difference;
        /// <summary>Message si la valeur sort des limites du point (null sinon).</summary>
        public string OutOfRange;
    }

    /// <summary>Points de mesure et documents de mesure / relevés de compteur (IK11).</summary>
    internal static class MeasurementService
    {
        public static List<MeasuringPoint> Points(string equip)
        {
            var list = new List<MeasuringPoint>();
            foreach (Row r in Sql.Rows("SELECT * FROM " + Db.T(Db.EquipPts) + " WHERE \"Code\" = " + Sql.Q(equip) +
                                       " AND ISNULL(\"U_Point\", '') <> '' ORDER BY \"LineId\""))
                list.Add(ToPoint(r));
            return list;
        }

        public static MeasuringPoint Point(string equip, string point)
        {
            Row r = Sql.First("SELECT * FROM " + Db.T(Db.EquipPts) + " WHERE \"Code\" = " + Sql.Q(equip) + " AND \"U_Point\" = " + Sql.Q(point));
            return r == null ? null : ToPoint(r);
        }

        private static MeasuringPoint ToPoint(Row r)
        {
            return new MeasuringPoint
            {
                Point = r.Str("U_Point"),
                Descr = r.Str("U_Descr"),
                Unit = r.Str("U_Unit"),
                IsCounter = r.Str("U_Counter") == "Y",
                Min = r.Dbl("U_MinVal"),
                Max = r.Dbl("U_MaxVal")
            };
        }

        /// <summary>Dernier relevé (ordre chronologique), ou null.</summary>
        public static Row LastReading(string equip, string point)
        {
            return Sql.First("SELECT TOP 1 \"U_Value\", \"U_MDate\", \"U_MTime\" FROM " + Db.T(Db.MeasDoc) +
                             " WHERE \"U_Equip\" = " + Sql.Q(equip) + " AND \"U_Point\" = " + Sql.Q(point) +
                             " ORDER BY \"U_MDate\" DESC, \"U_MTime\" DESC, \"Code\" DESC");
        }

        public static double CurrentCounter(string equip, string point)
        {
            if (string.IsNullOrEmpty(equip) || string.IsNullOrEmpty(point))
                return 0;
            Row r = LastReading(equip, point);
            return r?.Dbl("U_Value") ?? 0;
        }

        /// <summary>Enregistre un relevé ; un compteur ne peut pas diminuer.</summary>
        public static MeasurementResult Record(string equip, string point, DateTime date, int timeHhmm, double value, string remarks)
        {
            AuthService.Require(Perm.Exec, "saisir un relevé");
            return RecordData(equip, point, date, timeHhmm, value, remarks, 0);
        }

        /// <summary>Relevé sans contrôle d'autorisation (relevés automatiques de la production, srcDoc = ligne d'entrée).</summary>
        internal static MeasurementResult RecordData(string equip, string point, DateTime date, int timeHhmm, double value, string remarks, long srcDoc)
        {
            MeasuringPoint mp = Point(equip, point);
            if (mp == null)
                throw new InvalidOperationException("Point de mesure « " + point + " » introuvable sur l'équipement " + equip + ".");
            if (date > DateTime.Today)
                throw new InvalidOperationException("La date du relevé ne peut pas être dans le futur.");

            Row last = LastReading(equip, point);
            double diff = 0;
            if (last != null)
            {
                DateTime lastDate = last.Date("U_MDate") ?? DateTime.MinValue;
                int lastTime = last.Int("U_MTime");
                bool older = date < lastDate || (date == lastDate && timeHhmm < lastTime);
                if (mp.IsCounter && older)
                    throw new InvalidOperationException("Un relevé plus récent existe déjà (" + lastDate.ToString("dd/MM/yyyy") + ") : saisissez les relevés de compteur dans l'ordre chronologique.");
                diff = value - last.Dbl("U_Value");
                if (mp.IsCounter && diff < 0)
                    throw new InvalidOperationException("Un compteur ne peut pas diminuer : dernier relevé " +
                        last.Dbl("U_Value").ToString("N2", CultureInfo.GetCultureInfo("fr-FR")) + " " + mp.Unit + ".");
            }

            UserTable t = DiCompany.Instance.UserTables.Item(Db.MeasDoc);
            try
            {
                string code = Sql.NextCode(Db.MeasDoc);
                t.Code = code;
                t.Name = code;
                Fields f = t.UserFields.Fields;
                f.Item("U_Equip").Value = equip;
                f.Item("U_Point").Value = point;
                f.Item("U_MDate").Value = date;
                SetTime(f.Item("U_MTime"), date, timeHhmm);
                f.Item("U_Value").Value = value;
                f.Item("U_Diff").Value = diff;
                f.Item("U_Remarks").Value = NotificationService.Truncate(remarks, 200);
                f.Item("U_User").Value = DiCompany.UserCode;
                if (srcDoc > 0)
                    f.Item("U_SrcDoc").Value = checked((int)srcDoc);
                DiCompany.ThrowIfError(t.Add(), "Enregistrement du relevé");
            }
            finally
            {
                Marshal.ReleaseComObject(t);
            }

            var result = new MeasurementResult { Difference = diff };
            if (!mp.IsCounter && (mp.Min != 0 || mp.Max != 0))
            {
                if (mp.Min != 0 && value < mp.Min)
                    result.OutOfRange = mp.Descr + " : " + value + " " + mp.Unit + " sous la limite basse (" + mp.Min + ")";
                else if (mp.Max != 0 && value > mp.Max)
                    result.OutOfRange = mp.Descr + " : " + value + " " + mp.Unit + " au-dessus de la limite haute (" + mp.Max + ")";
            }
            return result;
        }

        /// <summary>Champ heure SAP (stocké HHMM) : le DI API attend une date-heure.</summary>
        private static void SetTime(Field field, DateTime date, int hhmm)
        {
            if (hhmm <= 0)
                return;
            int h = Math.Min(23, hhmm / 100), m = Math.Min(59, hhmm % 100);
            field.Value = date.Date.AddHours(h).AddMinutes(m);
        }

        /// <summary>Rattache l'avis créé au dernier relevé du point.</summary>
        public static void LinkNotification(string equip, string point, int notifEntry)
        {
            string code = Sql.ScalarStr("SELECT TOP 1 \"Code\" FROM " + Db.T(Db.MeasDoc) + " WHERE \"U_Equip\" = " + Sql.Q(equip) +
                                        " AND \"U_Point\" = " + Sql.Q(point) + " ORDER BY \"Code\" DESC");
            if (code == "")
                return;
            UserTable t = DiCompany.Instance.UserTables.Item(Db.MeasDoc);
            try
            {
                if (!t.GetByKey(code))
                    return;
                t.UserFields.Fields.Item("U_NotifNo").Value = notifEntry;
                DiCompany.ThrowIfError(t.Update(), "Rattachement de l'avis au relevé");
            }
            finally
            {
                Marshal.ReleaseComObject(t);
            }
        }
    }
}
