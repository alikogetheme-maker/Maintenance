using System;
using System.Collections.Generic;
using MaintenanceAddon.Core;
using MaintenanceAddon.Models;

namespace MaintenanceAddon.Services
{
    /// <summary>Informations utiles d'un équipement (lecture SQL).</summary>
    internal sealed class EquipmentInfo
    {
        public string Code = "";
        public string Name = "";
        public string Category = "";
        public string FuncLoc = "";
        public string WorkCtr = "";
        public string OcrCode = "";
        public string Whs = "";
        public string Status = "";
        public DateTime? WarrantyEnd;
        /// <summary>Garant : fournisseur de garantie, sinon fournisseur d'achat.</summary>
        public string WarrantyVendor = "";
        /// <summary>Prestataire de maintenance attitré.</summary>
        public string MaintVendor = "";

        public bool UnderWarranty(DateTime date)
        {
            return WarrantyEnd.HasValue && date.Date <= WarrantyEnd.Value;
        }

        public static EquipmentInfo Load(string code)
        {
            if (string.IsNullOrEmpty(code))
                return null;
            Row r = Sql.First(
                "SELECT e.\"Code\", e.\"Name\", e.\"U_Category\", e.\"U_FuncLoc\", e.\"U_WorkCtr\", e.\"U_Status\", " +
                " e.\"U_WarrEnd\", COALESCE(NULLIF(e.\"U_WarrVend\", ''), e.\"U_Vendor\", '') AS \"WarrV\", ISNULL(e.\"U_MntVend\", '') AS \"MntV\", " +
                " COALESCE(NULLIF(e.\"U_OcrCode\", ''), f.\"U_OcrCode\", '') AS \"Ocr\", " +
                " COALESCE(NULLIF(e.\"U_Whs\", ''), f.\"U_Whs\", '') AS \"Whs\" " +
                "FROM " + Db.T(Db.Equip) + " e LEFT JOIN " + Db.T(Db.FuncLoc) + " f ON f.\"Code\" = e.\"U_FuncLoc\" " +
                "WHERE e.\"Code\" = " + Sql.Q(code));
            if (r == null)
                return null;
            var info = new EquipmentInfo
            {
                Code = r.Str("Code"),
                Name = r.Str("Name"),
                Category = r.Str("U_Category"),
                FuncLoc = r.Str("U_FuncLoc"),
                WorkCtr = r.Str("U_WorkCtr"),
                OcrCode = r.Str("Ocr"),
                Whs = r.Str("Whs"),
                Status = r.Str("U_Status"),
                WarrantyEnd = r.Date("U_WarrEnd"),
                WarrantyVendor = r.Str("WarrV"),
                MaintVendor = r.Str("MntV")
            };

            // Héritage depuis les postes techniques supérieurs (comme SAP PM)
            string parent = info.FuncLoc == "" ? "" : Sql.ScalarStr("SELECT \"U_Parent\" FROM " + Db.T(Db.FuncLoc) + " WHERE \"Code\" = " + Sql.Q(info.FuncLoc));
            for (int i = 0; i < 20 && parent != "" && (info.OcrCode == "" || info.Whs == ""); i++)
            {
                Row p = Sql.First("SELECT \"U_Parent\", \"U_OcrCode\", \"U_Whs\" FROM " + Db.T(Db.FuncLoc) + " WHERE \"Code\" = " + Sql.Q(parent));
                if (p == null)
                    break;
                if (info.OcrCode == "") info.OcrCode = p.Str("U_OcrCode");
                if (info.Whs == "") info.Whs = p.Str("U_Whs");
                parent = p.Str("U_Parent");
            }
            return info;
        }

        /// <summary>Compte de charges : celui de la catégorie, sinon celui des paramètres.</summary>
        public string ExpenseAccount(Settings s)
        {
            string acct = string.IsNullOrEmpty(Category) ? "" :
                Sql.ScalarStr("SELECT \"U_ExpAcct\" FROM " + Db.T(Db.EqCat) + " WHERE \"Code\" = " + Sql.Q(Category));
            return string.IsNullOrEmpty(acct) ? s.ExpenseAccount : acct;
        }
    }

    /// <summary>Avis de maintenance (IW21 / IW22 / IW23).</summary>
    internal static class NotificationService
    {
        /// <summary>Libellé court d'un code de catalogue / poste / équipement (affichage).</summary>
        public static string NameOf(string table, string code)
        {
            if (string.IsNullOrEmpty(code))
                return "";
            return Sql.ScalarStr("SELECT \"Name\" FROM " + Db.T(table) + " WHERE \"Code\" = " + Sql.Q(code));
        }

        public static List<KeyValuePair<string, string>> CatalogCodes(string type)
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (Row r in Sql.Rows("SELECT \"Code\", \"Name\" FROM " + Db.T(Db.Catalog) +
                                       " WHERE \"U_Type\" = " + Sql.Q(type) + " AND ISNULL(\"U_Active\", 'Y') <> 'N' ORDER BY \"Code\""))
                list.Add(new KeyValuePair<string, string>(r.Str("Code"), r.Str("Name")));
            return list;
        }

        /// <summary>Crée l'ordre correctif de l'avis (IW22 « Créer ordre ») et passe l'avis « en cours ».</summary>
        public static int CreateOrder(int notifEntry)
        {
            AuthService.Require(Perm.Order, "créer un ordre");
            UdoData n = UdoData.Get(Obj.Notif, notifEntry);
            if (n.Str("U_Status") == NotifStatus.Completed)
                throw new InvalidOperationException("L'avis est terminé : rouvrez-le avant de créer un ordre.");
            int existing = (int)n.Dbl("U_OrderNo");
            if (existing > 0 && OrderService.Status(existing) != OrderStatus.Cancelled)
                throw new InvalidOperationException("L'avis est déjà rattaché à l'ordre " + OrderService.DocNum(existing) + ".");
            if (n.Str("U_Type") == NotifTypes.Activity)
                throw new InvalidOperationException("Un rapport d'activité (M3) documente un travail déjà fait : il ne génère pas d'ordre.");

            EquipmentInfo eq = EquipmentInfo.Load(n.Str("U_Equip"));
            Settings s = SettingsService.Load();
            DateTime start = n.Date("U_ReqStart") ?? DateTime.Today;
            DateTime end = n.Date("U_ReqEnd") ?? start.AddHours(s.HoursFor(n.Str("U_Priority")));
            string workCtr = n.Str("U_WorkCtr") != "" ? n.Str("U_WorkCtr") : eq?.WorkCtr ?? "";

            var draft = new OrderDraft
            {
                OrdType = OrderTypes.Corrective,
                Equip = n.Str("U_Equip"),
                FuncLoc = n.Str("U_FuncLoc") != "" ? n.Str("U_FuncLoc") : eq?.FuncLoc ?? "",
                NotifNo = notifEntry,
                Priority = n.Str("U_Priority"),
                Subject = n.Str("U_Subject"),
                Descr = n.Str("U_Descr"),
                WorkCtr = workCtr,
                OcrCode = eq?.OcrCode ?? "",
                Start = start,
                End = end < start ? start : end
            };
            // Garantie ou contrat : l'intervention revient au garant / au prestataire,
            // dans le délai d'intervention prévu au contrat.
            ServiceContext ctx = eq == null ? null : ServiceContext.For(eq.Code, start, OrderTypes.Corrective);
            bool external = ctx != null && (ctx.UnderWarranty || ctx.Contract != null) && ctx.DefaultVendor != "";
            if (ctx?.Contract != null && ctx.Contract.ResponseHours > 0)
            {
                DateTime sla = start.AddHours(ctx.Contract.ResponseHours).Date;
                if (sla < draft.End)
                    draft.End = sla < start ? start : sla;
            }

            // Opération par défaut (comme SAP PM) : à détailler dans l'ordre
            draft.Ops.Add(new OpDraft
            {
                OpNo = "0010",
                Descr = Truncate((external ? (ctx.UnderWarranty ? "Sous garantie - " : "Contrat " + ctx.Contract.Code + " - ") : "") + n.Str("U_Subject"), 100),
                WorkCtr = workCtr,
                CtrlKey = external ? ControlKeys.External : ControlKeys.Internal,
                Vendor = external ? ctx.DefaultVendor : "",
                PlanHrs = 0,
                NbPers = 1
            });

            return DiCompany.InTransaction(() =>
            {
                int orderEntry = OrderService.Create(draft);
                n.Set("U_OrderNo", orderEntry);
                n.Set("U_Status", NotifStatus.InProcess);
                n.Update();
                return orderEntry;
            });
        }

        /// <summary>Termine l'avis (NOCO). L'ordre éventuel doit être clôturé techniquement.</summary>
        public static void Complete(int notifEntry, DateTime date)
        {
            AuthService.Require(Perm.Notif, "terminer un avis");
            UdoData n = UdoData.Get(Obj.Notif, notifEntry);
            if (n.Str("U_Status") == NotifStatus.Completed)
                return;
            int order = (int)n.Dbl("U_OrderNo");
            if (order > 0)
            {
                string st = OrderService.Status(order);
                if (st == OrderStatus.Created || st == OrderStatus.Released)
                    throw new InvalidOperationException("L'ordre " + OrderService.DocNum(order) + " n'est pas clôturé techniquement : clôturez-le d'abord (l'avis sera terminé avec lui).");
            }
            CompleteData(n, date);
            n.Update();
        }

        /// <summary>Passe l'avis à NOCO dans l'objet chargé (fin de panne par défaut = date de fin).</summary>
        internal static void CompleteData(UdoData n, DateTime date)
        {
            n.Set("U_Status", NotifStatus.Completed);
            n.Set("U_ComplDt", date);
            if (n.Str("U_Breakdwn") == "Y" && n.Date("U_MalfEnD") == null)
                n.Set("U_MalfEnD", date);
        }

        public static void Reopen(int notifEntry)
        {
            AuthService.Require(Perm.Notif, "rouvrir un avis");
            ReopenData(notifEntry);
        }

        /// <summary>Réouverture sans contrôle d'autorisation (annulation de la clôture technique de l'ordre).</summary>
        internal static void ReopenData(int notifEntry)
        {
            UdoData n = UdoData.Get(Obj.Notif, notifEntry);
            if (n.Str("U_Status") != NotifStatus.Completed)
                return;
            int order = (int)n.Dbl("U_OrderNo");
            bool active = order > 0 && OrderService.Status(order) != OrderStatus.Cancelled;
            n.Set("U_Status", active ? NotifStatus.InProcess : NotifStatus.Outstanding);
            n.ClearDate("U_ComplDt");
            n.Update();
        }

        /// <summary>Statut de l'avis lié à un ordre (appelé par les changements de statut de l'ordre).</summary>
        internal static void OnOrderStatus(int notifEntry, string orderStatus, int orderEntry, DateTime date)
        {
            if (notifEntry <= 0)
                return;
            UdoData n = UdoData.Get(Obj.Notif, notifEntry);
            switch (orderStatus)
            {
                case OrderStatus.Released:
                    if (n.Str("U_Status") == NotifStatus.Completed)
                        return;
                    n.Set("U_Status", NotifStatus.InProcess);
                    break;
                case OrderStatus.TechCompleted:
                    if (n.Str("U_Status") == NotifStatus.Completed)
                        return;
                    CompleteData(n, date);
                    break;
                case OrderStatus.Cancelled:
                    n.Set("U_Status", NotifStatus.Outstanding);
                    n.Set("U_OrderNo", 0);
                    break;
                default:
                    return;
            }
            n.Update();
        }

        /// <summary>Avis de panne créé depuis une mesure hors limites.</summary>
        public static int CreateFromMeasurement(string equip, string point, string text)
        {
            EquipmentInfo eq = EquipmentInfo.Load(equip);
            Settings s = SettingsService.Load();
            UdoData n = UdoData.New(Obj.Notif);
            n.Set("U_Type", NotifTypes.Request);
            n.Set("U_Status", NotifStatus.Outstanding);
            n.Set("U_Equip", equip);
            n.Set("U_FuncLoc", eq?.FuncLoc ?? "");
            n.Set("U_WorkCtr", eq?.WorkCtr ?? "");
            n.Set("U_Priority", "2");
            n.Set("U_Subject", Truncate(text, 100));
            n.Set("U_Point", point);
            n.Set("U_ReportBy", DiCompany.UserCode);
            n.Set("U_RepDate", DateTime.Today);
            n.Set("U_ReqStart", DateTime.Today);
            n.Set("U_ReqEnd", DateTime.Today.AddHours(s.HoursFor("2")).Date);
            n.Set("U_Breakdwn", "N");
            ServiceContext ctx = ServiceContext.For(equip, DateTime.Today);
            n.Set("U_UnderWar", ctx != null && ctx.UnderWarranty ? "Y" : "N");
            n.Set("U_Contract", ctx?.Contract?.Code ?? "");
            return n.Add();
        }

        internal static string Truncate(string text, int max)
        {
            text = text ?? "";
            return text.Length <= max ? text : text.Substring(0, max);
        }
    }
}
