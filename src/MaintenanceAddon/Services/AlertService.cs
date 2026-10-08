using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using SAPbobsCOM;
using MaintenanceAddon.Core;
using MaintenanceAddon.Models;

namespace MaintenanceAddon.Services
{
    /// <summary>
    /// Alertes envoyées par la messagerie interne de SAP (boîte de réception
    /// des utilisateurs) : avis urgent, et chaque jour la liste des préventifs
    /// en retard, ordres en retard, garanties et contrats qui expirent,
    /// équipements non revenus de chez le prestataire.
    /// </summary>
    internal static class AlertService
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        /// <summary>Codes utilisateurs existants et actifs, séparés par des virgules (erreur si inconnu).</summary>
        public static string NormalizeUsers(string users)
        {
            var list = new List<string>();
            foreach (string u in (users ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string code = Sql.ScalarStr("SELECT \"USER_CODE\" FROM \"OUSR\" WHERE \"USER_CODE\" = " + Sql.Q(u.Trim()));
                if (code == "")
                    throw new InvalidOperationException("Destinataire des alertes : utilisateur SAP inconnu « " + u.Trim() + " ».");
                if (!list.Contains(code))
                    list.Add(code);
            }
            string result = string.Join(",", list);
            if (result.Length > 254)
                throw new InvalidOperationException("Trop de destinataires d'alertes (254 caractères au plus).");
            return result;
        }

        private static List<string> Recipients(Settings s, params string[] extra)
        {
            var list = (s.AlertUsers ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(u => u.Trim()).ToList();
            foreach (string u in extra)
                if (!string.IsNullOrEmpty(u) && !list.Contains(u))
                    list.Add(u);
            // Utilisateurs verrouillés exclus (SAP refuserait le message)
            return list.Where(u => Sql.Exists("SELECT 1 FROM \"OUSR\" WHERE \"USER_CODE\" = " + Sql.Q(u) + " AND ISNULL(\"Locked\", 'N') <> 'Y'")).ToList();
        }

        /// <summary>Message interne SAP ; renvoie son n° (OALR) ou 0 s'il n'y a pas de destinataire.</summary>
        public static int Send(string subject, string text, IList<string> users)
        {
            if (users == null || users.Count == 0)
                return 0;
            Company company = DiCompany.Instance;
            Messages m = (Messages)company.GetBusinessObject(BoObjectTypes.oMessages);
            try
            {
                m.Subject = NotificationService.Truncate(subject, 254);
                m.MessageText = text;
                m.Priority = BoMsgPriorities.pr_High;
                for (int i = 0; i < users.Count; i++)
                {
                    if (i > 0)
                        m.Recipients.Add();
                    m.Recipients.SetCurrentLine(i);
                    m.Recipients.UserCode = users[i];
                    m.Recipients.NameTo = users[i];
                    m.Recipients.SendInternal = BoYesNoEnum.tYES;
                }
                DiCompany.ThrowIfError(m.Add(), "Envoi de l'alerte de maintenance");
                int code;
                int.TryParse(company.GetNewObjectKey(), NumberStyles.Integer, CultureInfo.InvariantCulture, out code);
                return code;
            }
            finally
            {
                Marshal.ReleaseComObject(m);
            }
        }

        /// <summary>
        /// Avis urgent (priorité 1 ou arrêt de l'équipement) : message aux
        /// destinataires des alertes ; renvoie le n° du message ou 0.
        /// </summary>
        public static int NotifyUrgent(int notifEntry)
        {
            Row n = Sql.First("SELECT n.*, e.\"Name\" AS \"EqName\" FROM " + Db.T(Db.Notif) + " n LEFT JOIN " + Db.T(Db.Equip) + " e ON e.\"Code\" = n.\"U_Equip\" " +
                              "WHERE n.\"DocEntry\" = " + notifEntry);
            if (n == null || (n.Str("U_Priority") != "1" && n.Str("U_Breakdwn") != "Y"))
                return 0;
            Settings s = SettingsService.Load();
            var text = new StringBuilder();
            text.AppendLine((n.Str("U_Breakdwn") == "Y" ? "Équipement à l'arrêt" : "Avis de priorité très élevée") + " - " + NotifTypes.List.Caption(n.Str("U_Type")));
            text.AppendLine("Avis n° " + n.Str("DocNum") + " du " + n.Date("U_RepDate")?.ToString("dd/MM/yyyy") + ", déclaré par " + n.Str("U_ReportBy"));
            if (n.Str("U_Equip") != "")
                text.AppendLine("Équipement : " + n.Str("U_Equip") + " - " + n.Str("EqName"));
            if (n.Str("U_FuncLoc") != "")
                text.AppendLine("Poste technique : " + n.Str("U_FuncLoc") + " - " + NotificationService.NameOf(Db.FuncLoc, n.Str("U_FuncLoc")));
            text.AppendLine("Description : " + n.Str("U_Subject"));
            ProdOrderInfo of = ProductionService.Load(n.Int("U_ProdOrd"));
            if (of != null)
                text.AppendLine("Production : " + of.Label() + (n.Str("U_LineStop") == "Y" ? " - LIGNE DE PRODUCTION ARRÊTÉE" : ""));
            ServiceContext ctx = n.Str("U_Equip") == "" ? null : ServiceContext.For(n.Str("U_Equip"), DateTime.Today, OrderTypes.Corrective);
            if (ctx != null && ctx.Banner() != "")
                text.AppendLine(ctx.Banner());
            text.AppendLine();
            text.AppendLine("Ouvrez l'avis dans Modules → Maintenance → Avis de maintenance (Rechercher n° " + n.Str("DocNum") + ").");
            return Send("Maintenance - avis urgent n° " + n.Str("DocNum") + " - " + n.Str("U_Subject"), text.ToString(), Recipients(s));
        }

        /// <summary>Contenu des alertes du jour (vide s'il n'y a rien à signaler).</summary>
        public static string DailyText(DateTime today, int days)
        {
            var text = new StringBuilder();
            DateTime limit = today.AddDays(days);

            var plans = PlanService.Overview(today).Where(p => p.IsDue && p.OpenOrder == 0 && p.DueDate.HasValue && p.DueDate.Value < today).ToList();
            Section(text, "Entretiens préventifs en retard (à appeler dans l'ordonnancement)", plans.Select(p =>
                p.PlanCode + " - " + p.PlanName + (p.Equip != "" ? " (" + p.Equip + ")" : "") + " : échéance du " + p.DueDate.Value.ToString("dd/MM/yyyy")));

            Section(text, "Ordres en retard (fin prévue dépassée)", Sql.Rows(
                "SELECT o.\"DocNum\", o.\"U_Equip\", o.\"U_Subject\", o.\"U_EndDt\", o.\"U_Status\" FROM " + Db.T(Db.Order) + " o " +
                "WHERE o.\"U_Status\" IN ('CRTD', 'REL') AND o.\"U_EndDt\" < " + Sql.D(today) + " ORDER BY o.\"U_Priority\", o.\"U_EndDt\"")
                .Select(r => "Ordre " + r.Str("DocNum") + " - " + r.Str("U_Subject") + (r.Str("U_Equip") != "" ? " (" + r.Str("U_Equip") + ")" : "") +
                             " : fin prévue le " + r.Date("U_EndDt")?.ToString("dd/MM/yyyy") + ", statut " + OrderStatus.List.Caption(r.Str("U_Status"))));

            Section(text, "Garanties qui expirent dans les " + days + " jours", Sql.Rows(
                "SELECT \"Code\", \"Name\", \"U_WarrEnd\" FROM " + Db.T(Db.Equip) + " WHERE ISNULL(\"U_Status\", 'A') <> 'S' " +
                "AND \"U_WarrEnd\" BETWEEN " + Sql.D(today) + " AND " + Sql.D(limit) + " ORDER BY \"U_WarrEnd\"")
                .Select(r => r.Str("Code") + " - " + r.Str("Name") + " : garantie jusqu'au " + r.Date("U_WarrEnd")?.ToString("dd/MM/yyyy") +
                             " (faire contrôler l'équipement par le garant avant cette date)"));

            Section(text, "Contrats de maintenance à décider (fin ou préavis de résiliation dans les " + days + " jours)", Sql.Rows(
                "SELECT c.\"Code\", c.\"Name\", c.\"U_EndDt\", DATEADD(day, -ISNULL(c.\"U_Notice\", 0), c.\"U_EndDt\") AS \"Notice\" FROM " + Db.T(Db.Contract) + " c " +
                "WHERE ISNULL(c.\"U_Active\", 'Y') = 'Y' AND c.\"U_EndDt\" >= " + Sql.D(today) +
                " AND DATEADD(day, -ISNULL(c.\"U_Notice\", 0), c.\"U_EndDt\") <= " + Sql.D(limit) + " ORDER BY c.\"U_EndDt\"")
                .Select(r => "Contrat " + r.Str("Code") + " - " + r.Str("Name") + " : fin le " + r.Date("U_EndDt")?.ToString("dd/MM/yyyy") +
                             (r.Date("Notice") != r.Date("U_EndDt") ? ", résilier avant le " + r.Date("Notice")?.ToString("dd/MM/yyyy") : "")));

            Section(text, "Équipements non revenus de chez le prestataire à la date prévue", Sql.Rows(
                "SELECT s.\"U_Equip\", s.\"U_ExpRet\", ISNULL(b.\"CardName\", s.\"U_Vendor\") AS \"Vendor\" FROM " + Db.T(Db.Ship) + " s " +
                "LEFT JOIN \"OCRD\" b ON b.\"CardCode\" = s.\"U_Vendor\" WHERE s.\"U_Status\" = 'O' AND s.\"U_ExpRet\" < " + Sql.D(today) + " ORDER BY s.\"U_ExpRet\"")
                .Select(r => r.Str("U_Equip") + " - " + NotificationService.NameOf(Db.Equip, r.Str("U_Equip")) + " : chez " + r.Str("Vendor") +
                             ", retour prévu le " + r.Date("U_ExpRet")?.ToString("dd/MM/yyyy")));

            return text.ToString();
        }

        private static void Section(StringBuilder text, string title, IEnumerable<string> lines)
        {
            List<string> list = lines.ToList();
            if (list.Count == 0)
                return;
            text.AppendLine(title + " : " + list.Count);
            // Le message reste lisible : au-delà, le rapport correspondant donne la liste complète
            foreach (string l in list.Take(30))
                text.AppendLine("  - " + l);
            if (list.Count > 30)
                text.AppendLine("  ... et " + (list.Count - 30) + " autre(s) : voir Maintenance → Rapports.");
            text.AppendLine();
        }

        /// <summary>
        /// Alertes du jour : envoyées une seule fois par jour (au premier démarrage
        /// de l'add-on), ou à la demande (force). Renvoie le n° du message ou 0.
        /// </summary>
        public static int SendDaily(DateTime today, bool force)
        {
            Settings s = SettingsService.Load();
            if (!force && s.AlertDate.HasValue && s.AlertDate.Value >= today.Date)
                return 0;
            List<string> users = Recipients(s);
            if (users.Count == 0)
                return 0;
            string text = DailyText(today, s.AlertDays);
            int code = text == "" ? 0 : Send("Maintenance - alertes du " + today.ToString("dd/MM/yyyy", Fr), text, users);
            MarkSent(today);
            return code;
        }

        private static void MarkSent(DateTime today)
        {
            UserTable table = DiCompany.Instance.UserTables.Item(Db.Setup);
            try
            {
                if (!table.GetByKey(Db.SetupCode))
                    return;
                table.UserFields.Fields.Item("U_AlertDt").Value = today.Date;
                DiCompany.ThrowIfError(table.Update(), "Date d'envoi des alertes");
            }
            finally
            {
                Marshal.ReleaseComObject(table);
            }
        }
    }
}
