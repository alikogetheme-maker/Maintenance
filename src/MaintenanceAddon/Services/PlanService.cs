using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using SAPbobsCOM;
using MaintenanceAddon.Core;
using MaintenanceAddon.Models;

namespace MaintenanceAddon.Services
{
    /// <summary>Situation d'un plan pour l'ordonnancement (vue IP24).</summary>
    internal sealed class PlanDue
    {
        public string PlanCode;
        public string PlanName;
        public string Type;
        public string Basis;
        public string Equip;
        public DateTime? DueDate;
        public double DueCounter;
        public double CurrentCounter;
        public bool IsDue;
        public int OpenOrder;
        public string State;
    }

    /// <summary>
    /// Plans de maintenance préventive (IP41 / IP42) et ordonnancement
    /// (IP10 / IP30) : calcul des échéances, création des ordres, suivi des appels.
    /// </summary>
    internal static class PlanService
    {
        public static DateTime AddCycle(DateTime date, int cycle, string unit)
        {
            switch (unit)
            {
                case "D": return date.AddDays(cycle);
                case "W": return date.AddDays(7 * cycle);
                case "Y": return date.AddYears(cycle);
                default: return date.AddMonths(cycle);
            }
        }

        /// <summary>Plans actifs avec leur prochaine échéance, triés par urgence.</summary>
        public static List<PlanDue> Overview(DateTime horizon)
        {
            var result = new List<PlanDue>();
            foreach (Row p in Sql.Rows("SELECT * FROM " + Db.T(Db.Plan) + " WHERE ISNULL(\"U_Active\", 'Y') = 'Y' ORDER BY \"Code\""))
            {
                var d = new PlanDue
                {
                    PlanCode = p.Str("Code"),
                    PlanName = p.Str("Name"),
                    Type = p.Str("U_Type"),
                    Basis = p.Str("U_Basis"),
                    Equip = p.Str("U_Equip")
                };
                d.OpenOrder = OpenCallOrder(d.PlanCode);

                if (d.Type == PlanTypes.Counter)
                {
                    d.CurrentCounter = MeasurementService.CurrentCounter(d.Equip, p.Str("U_Point"));
                    d.DueCounter = NextCounter(p);
                    d.IsDue = p.Dbl("U_CycCount") > 0 && d.CurrentCounter >= d.DueCounter - p.Dbl("U_LeadCnt");
                    d.DueDate = EstimateCounterDate(d.Equip, p.Str("U_Point"), d.CurrentCounter, d.DueCounter);
                }
                else
                {
                    d.DueDate = NextDate(p);
                    d.IsDue = d.DueDate.HasValue && d.DueDate.Value.AddDays(-p.Int("U_LeadDays")) <= horizon;
                }

                if (d.OpenOrder > 0 && d.Basis == SchedBasis.Completion)
                {
                    d.IsDue = false;
                    d.State = "Ordre " + OrderService.DocNum(d.OpenOrder) + " en cours";
                }
                else if (d.IsDue)
                    d.State = "À appeler";
                else
                    d.State = "Pas encore dû";
                result.Add(d);
            }
            result.Sort((a, b) =>
            {
                int c = b.IsDue.CompareTo(a.IsDue);
                if (c != 0)
                    return c;
                return (a.DueDate ?? DateTime.MaxValue).CompareTo(b.DueDate ?? DateTime.MaxValue);
            });
            return result;
        }

        private static DateTime? NextDate(Row p)
        {
            DateTime? next = p.Date("U_NextDate");
            if (next.HasValue)
                return next;
            DateTime? start = p.Date("U_StartDate");
            return start.HasValue ? AddCycle(start.Value, Math.Max(1, p.Int("U_Cycle")), p.Str("U_CycUnit")) : (DateTime?)null;
        }

        private static double NextCounter(Row p)
        {
            double next = p.Dbl("U_NextCnt");
            return next > 0 ? next : p.Dbl("U_StartCnt") + p.Dbl("U_CycCount");
        }

        /// <summary>Date estimée d'atteinte du compteur (estimation annuelle du point de mesure).</summary>
        private static DateTime? EstimateCounterDate(string equip, string point, double current, double due)
        {
            double perYear = Sql.ScalarDbl("SELECT \"U_AnnEst\" FROM " + Db.T(Db.EquipPts) +
                                           " WHERE \"Code\" = " + Sql.Q(equip) + " AND \"U_Point\" = " + Sql.Q(point));
            if (perYear <= 0)
                return null;
            double days = Math.Max(0, (due - current) / perYear * 365.0);
            return DateTime.Today.AddDays(Math.Min(days, 3650));
        }

        /// <summary>Ordre ouvert (appel en cours) du plan, ou 0.</summary>
        public static int OpenCallOrder(string planCode)
        {
            return (int)Sql.ScalarDbl("SELECT MAX(\"U_OrderNo\") FROM " + Db.T(Db.Call) +
                                      " WHERE \"U_PlanCode\" = " + Sql.Q(planCode) + " AND \"U_Status\" = " + Sql.Q(CallStatus.Open));
        }

        /// <summary>Appel du plan : crée l'ordre préventif depuis la gamme ; renvoie le DocEntry de l'ordre.</summary>
        public static int Call(string planCode)
        {
            UdoData p = UdoData.Get(Obj.Plan, planCode);
            if (p.Str("U_Active") == "N")
                throw new InvalidOperationException("Le plan " + planCode + " est inactif.");
            string basis = p.Str("U_Basis");
            if (basis == SchedBasis.Completion && OpenCallOrder(planCode) > 0)
                throw new InvalidOperationException("Le plan " + planCode + " a déjà un ordre en cours : la prochaine échéance sera calculée à sa clôture technique.");

            bool counter = p.Str("U_Type") == PlanTypes.Counter;
            Row pr = Sql.First("SELECT * FROM " + Db.T(Db.Plan) + " WHERE \"Code\" = " + Sql.Q(planCode));
            DateTime? dueDate = counter ? (DateTime?)null : NextDate(pr);
            double dueCnt = counter ? NextCounter(pr) : 0;
            if (!counter && dueDate == null)
                throw new InvalidOperationException("Renseignez la date de début du cycle du plan " + planCode + ".");

            string equip = p.Str("U_Equip");
            EquipmentInfo eq = EquipmentInfo.Load(equip);
            Settings s = SettingsService.Load();
            DateTime start = dueDate.HasValue && dueDate.Value > DateTime.Today ? dueDate.Value : DateTime.Today;
            string priority = p.Str("U_Priority") == "" ? "3" : p.Str("U_Priority");

            var draft = new OrderDraft
            {
                OrdType = p.Str("U_OrdType") == "" ? OrderTypes.Preventive : p.Str("U_OrdType"),
                Equip = equip,
                FuncLoc = p.Str("U_FuncLoc") != "" ? p.Str("U_FuncLoc") : eq?.FuncLoc ?? "",
                PlanCode = planCode,
                CallDue = dueDate ?? DateTime.Today,
                TaskList = p.Str("U_TaskList"),
                Priority = priority,
                Subject = p.Str("Name") + (counter ? " (à " + dueCnt.ToString("N0", CultureInfo.GetCultureInfo("fr-FR")) + ")" : ""),
                Descr = p.Str("U_Remarks"),
                WorkCtr = p.Str("U_WorkCtr") != "" ? p.Str("U_WorkCtr") : eq?.WorkCtr ?? "",
                OcrCode = eq?.OcrCode ?? "",
                Start = start,
                End = start.AddHours(s.HoursFor(priority)).Date
            };
            if (draft.TaskList != "")
                OrderService.LoadTaskList(draft.TaskList, draft.Ops, draft.Comps);
            if (draft.Ops.Count == 0)
                draft.Ops.Add(new OpDraft { OpNo = "0010", Descr = NotificationService.Truncate(p.Str("Name"), 100), WorkCtr = draft.WorkCtr });

            return DiCompany.InTransaction(() =>
            {
                int order = OrderService.Create(draft);
                AddCall(planCode, dueDate, dueCnt, order, CallStatus.Open);
                if (basis != SchedBasis.Completion)
                    Advance(p, counter, dueDate, dueCnt);
                else if (p.Date("U_NextDate") == null && dueDate.HasValue)
                {
                    p.Set("U_NextDate", dueDate.Value);
                    p.Update();
                }
                return order;
            });
        }

        /// <summary>Saut d'échéance : aucune intervention, l'échéance suivante est calculée.</summary>
        public static void Skip(string planCode)
        {
            UdoData p = UdoData.Get(Obj.Plan, planCode);
            if (OpenCallOrder(planCode) > 0 && p.Str("U_Basis") == SchedBasis.Completion)
                throw new InvalidOperationException("Le plan a un ordre en cours : clôturez-le ou annulez-le.");
            bool counter = p.Str("U_Type") == PlanTypes.Counter;
            Row pr = Sql.First("SELECT * FROM " + Db.T(Db.Plan) + " WHERE \"Code\" = " + Sql.Q(planCode));
            DateTime? dueDate = counter ? (DateTime?)null : NextDate(pr);
            double dueCnt = counter ? NextCounter(pr) : 0;
            DiCompany.InTransaction(() =>
            {
                AddCall(planCode, dueDate, dueCnt, 0, CallStatus.Skipped);
                Advance(p, counter, dueDate, dueCnt);
            });
        }

        private static void Advance(UdoData p, bool counter, DateTime? dueDate, double dueCnt)
        {
            if (counter)
                p.Set("U_NextCnt", dueCnt + p.Dbl("U_CycCount"));
            else if (dueDate.HasValue)
                p.Set("U_NextDate", AddCycle(dueDate.Value, Math.Max(1, (int)p.Dbl("U_Cycle")), p.Str("U_CycUnit")));
            p.Update();
        }

        /// <summary>Clôture technique d'un ordre de plan : appel réalisé, échéance suivante si base « clôture ».</summary>
        internal static void OnOrderCompleted(int orderEntry, string planCode, DateTime date)
        {
            SetCallStatus(orderEntry, CallStatus.Done, date);
            if (string.IsNullOrEmpty(planCode))
                return;

            UdoData p = UdoData.Get(Obj.Plan, planCode);
            bool counter = p.Str("U_Type") == PlanTypes.Counter;
            double reading = counter ? MeasurementService.CurrentCounter(p.Str("U_Equip"), p.Str("U_Point")) : 0;
            p.Set("U_LastDate", date);
            if (counter)
                p.Set("U_LastCnt", reading);
            if (p.Str("U_Basis") == SchedBasis.Completion)
            {
                if (counter)
                    p.Set("U_NextCnt", reading + p.Dbl("U_CycCount"));
                else
                    p.Set("U_NextDate", AddCycle(date, Math.Max(1, (int)p.Dbl("U_Cycle")), p.Str("U_CycUnit")));
            }
            p.Update();
        }

        internal static void OnOrderReopened(int orderEntry)
        {
            SetCallStatus(orderEntry, CallStatus.Open, null);
        }

        internal static void OnOrderCancelled(int orderEntry)
        {
            SetCallStatus(orderEntry, CallStatus.Skipped, DateTime.Today);
        }

        private static void AddCall(string planCode, DateTime? dueDate, double dueCnt, int order, string status)
        {
            UserTable t = DiCompany.Instance.UserTables.Item(Db.Call);
            try
            {
                string code = Sql.NextCode(Db.Call);
                t.Code = code;
                t.Name = code;
                Fields f = t.UserFields.Fields;
                f.Item("U_PlanCode").Value = planCode;
                if (dueDate.HasValue)
                    f.Item("U_DueDate").Value = dueDate.Value;
                f.Item("U_DueCnt").Value = dueCnt;
                f.Item("U_CallDate").Value = DateTime.Today;
                f.Item("U_OrderNo").Value = order;
                f.Item("U_Status").Value = status;
                if (status == CallStatus.Skipped)
                    f.Item("U_DoneDate").Value = DateTime.Today;
                f.Item("U_User").Value = DiCompany.UserCode;
                DiCompany.ThrowIfError(t.Add(), "Enregistrement de l'appel du plan " + planCode);
            }
            finally
            {
                Marshal.ReleaseComObject(t);
            }
        }

        private static void SetCallStatus(int orderEntry, string status, DateTime? doneDate)
        {
            foreach (Row r in Sql.Rows("SELECT \"Code\" FROM " + Db.T(Db.Call) + " WHERE \"U_OrderNo\" = " + orderEntry))
            {
                UserTable t = DiCompany.Instance.UserTables.Item(Db.Call);
                try
                {
                    if (!t.GetByKey(r.Str("Code")))
                        continue;
                    t.UserFields.Fields.Item("U_Status").Value = status;
                    if (doneDate.HasValue)
                        t.UserFields.Fields.Item("U_DoneDate").Value = doneDate.Value;
                    DiCompany.ThrowIfError(t.Update(), "Mise à jour de l'appel de plan");
                }
                finally
                {
                    Marshal.ReleaseComObject(t);
                }
            }
        }
    }
}
