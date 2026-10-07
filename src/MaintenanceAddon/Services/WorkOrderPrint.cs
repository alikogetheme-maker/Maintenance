using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using MaintenanceAddon.Core;
using MaintenanceAddon.Models;

namespace MaintenanceAddon.Services
{
    /// <summary>
    /// Bon de travail du technicien : page HTML ouverte dans le navigateur pour
    /// l'imprimer (appareil, lieu, travaux, pièces, sécurité, cases à remplir).
    /// </summary>
    internal static class WorkOrderPrint
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private static string E(string text)
        {
            return WebUtility.HtmlEncode(text ?? "");
        }

        private static string Multi(string text)
        {
            return E(text).Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "<br>");
        }

        private static string D(DateTime? d)
        {
            return d.HasValue ? d.Value.ToString("dd/MM/yyyy", Fr) : "";
        }

        private static string Q(double v)
        {
            return v.ToString("0.##", Fr);
        }

        /// <summary>Enregistre le bon dans le dossier temporaire ; renvoie le chemin du fichier.</summary>
        public static string Save(int orderEntry)
        {
            string html = Html(orderEntry);
            string dir = Path.Combine(Path.GetTempPath(), "Maintenance");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "Bon-de-travail-OM" + OrderService.DocNum(orderEntry) + ".html");
            File.WriteAllText(path, html, new UTF8Encoding(true));
            return path;
        }

        public static string Html(int orderEntry)
        {
            Row o = Sql.First("SELECT * FROM " + Db.T(Db.Order) + " WHERE \"DocEntry\" = " + orderEntry);
            if (o == null)
                throw new InvalidOperationException("Ordre introuvable.");
            Row eq = o.Str("U_Equip") == "" ? null : Sql.First("SELECT * FROM " + Db.T(Db.Equip) + " WHERE \"Code\" = " + Sql.Q(o.Str("U_Equip")));
            Row fl = o.Str("U_FuncLoc") == "" ? null : Sql.First("SELECT * FROM " + Db.T(Db.FuncLoc) + " WHERE \"Code\" = " + Sql.Q(o.Str("U_FuncLoc")));
            Row n = o.Int("U_NotifNo") == 0 ? null : Sql.First("SELECT * FROM " + Db.T(Db.Notif) + " WHERE \"DocEntry\" = " + o.Int("U_NotifNo"));
            string company = Sql.ScalarStr("SELECT \"CompnyName\" FROM \"OADM\"");
            string num = o.Str("DocNum");

            var h = new StringBuilder();
            h.Append("<!doctype html><html lang=\"fr\"><head><meta charset=\"utf-8\"><title>Bon de travail OM ").Append(E(num)).Append("</title><style>")
             .Append("body{font-family:Arial,Helvetica,sans-serif;font-size:12px;color:#000;margin:18px}")
             .Append("h1{font-size:18px;margin:0}h2{font-size:13px;background:#e8e8e8;padding:4px 6px;margin:14px 0 6px}")
             .Append("table{border-collapse:collapse;width:100%}td,th{border:1px solid #888;padding:4px 5px;vertical-align:top;text-align:left}")
             .Append("th{background:#f2f2f2;font-weight:bold}.k{width:22%;background:#f7f7f7}.blank{height:22px}.box{display:inline-block;width:12px;height:12px;border:1px solid #000}")
             .Append(".head td{border:none;padding:0}.right{text-align:right}.warn{border:2px solid #000;padding:6px;margin-top:8px;font-weight:bold}")
             .Append(".lines div{border-bottom:1px solid #888;height:22px}.sign td{height:60px;width:33%}")
             .Append("@media print{.noprint{display:none}body{margin:8mm}}")
             .Append("</style></head><body>");
            h.Append("<div class=\"noprint\" style=\"margin-bottom:10px\"><button onclick=\"window.print()\">Imprimer</button></div>");
            h.Append("<table class=\"head\"><tr><td><h1>BON DE TRAVAIL - Ordre n° ").Append(E(num)).Append("</h1>")
             .Append(E(OrderTypes.List.Caption(o.Str("U_OrdType")))).Append(" - priorité ").Append(E(Priorities.List.Caption(o.Str("U_Priority"))))
             .Append("</td><td class=\"right\">").Append(E(company)).Append("<br>Imprimé le ").Append(DateTime.Now.ToString("dd/MM/yyyy HH:mm", Fr)).Append("</td></tr></table>");

            // ---- Travail demandé --------------------------------------------
            h.Append("<h2>Travail demandé</h2><table>");
            Line(h, "Description", "<b>" + E(o.Str("U_Subject")) + "</b>" + (o.Str("U_Descr") == "" ? "" : "<br>" + Multi(o.Str("U_Descr"))));
            Line(h, "Dates prévues", "du " + D(o.Date("U_StartDt")) + " au " + D(o.Date("U_EndDt")));
            Line(h, "Statut", E(OrderStatus.List.Caption(o.Str("U_Status"))) + (o.Str("U_Respons") != "" ? " - responsable " + E(o.Str("U_Respons")) : ""));
            if (n != null)
                Line(h, "Avis d'origine", "n° " + E(n.Str("DocNum")) + " du " + D(n.Date("U_RepDate")) + ", déclaré par " + E(n.Str("U_ReportBy")) +
                                          (n.Str("U_Breakdwn") == "Y" ? " - <b>équipement à l'arrêt</b> depuis le " + D(n.Date("U_MalfStD")) : ""));
            if (o.Str("U_PlanCode") != "")
                Line(h, "Plan d'entretien", E(o.Str("U_PlanCode") + " - " + NotificationService.NameOf(Db.Plan, o.Str("U_PlanCode"))) + ", échéance du " + D(o.Date("U_CallDue")));
            h.Append("</table>");

            // ---- Équipement et lieu -----------------------------------------
            h.Append("<h2>Équipement et lieu d'intervention</h2><table>");
            if (eq != null)
            {
                Line(h, "Équipement", "<b>" + E(eq.Str("Code") + " - " + eq.Str("Name")) + "</b>" +
                                      (eq.Str("U_Critic") != "" ? " (criticité " + E(Criticality.List.Caption(eq.Str("U_Critic"))) + ")" : ""));
                var tech = new List<string>();
                if (eq.Str("U_Manuf") != "") tech.Add("fabricant " + eq.Str("U_Manuf"));
                if (eq.Str("U_Model") != "") tech.Add("modèle " + eq.Str("U_Model"));
                if (eq.Str("U_SerialNo") != "") tech.Add("n° de série " + eq.Str("U_SerialNo"));
                if (eq.Str("U_ItemCode") != "") tech.Add("article " + eq.Str("U_ItemCode"));
                if (tech.Count > 0)
                    Line(h, "Identification", E(string.Join(", ", tech)));
            }
            if (fl != null)
                Line(h, "Lieu", E(fl.Str("Code") + " - " + fl.Str("Name")) + (fl.Str("U_Location") != "" ? "<br>" + E(fl.Str("U_Location")) : ""));
            ServiceContext ctx = eq == null ? null : ServiceContext.For(eq.Str("Code"), o.Date("U_StartDt") ?? DateTime.Today, o.Str("U_OrdType"));
            if (ctx != null && ctx.Banner() != "")
                Line(h, "Garantie / contrat", E(ctx.Banner()));
            h.Append("</table>");

            // ---- Sécurité ---------------------------------------------------
            if (o.Str("U_Safety") != "")
                h.Append("<div class=\"warn\">CONSIGNES DE SÉCURITÉ<br>").Append(Multi(o.Str("U_Safety"))).Append("</div>");

            // ---- Opérations -------------------------------------------------
            h.Append("<h2>Opérations</h2><table><tr><th>Op.</th><th>Description</th><th>Poste</th><th>Prévu (h)</th><th>Réalisé (h)</th><th>Intervenant</th><th>Fait</th></tr>");
            foreach (Row r in Sql.Rows("SELECT * FROM " + Db.T(Db.OrderOps) + " WHERE \"DocEntry\" = " + orderEntry + " ORDER BY \"U_OpNo\", \"LineId\""))
            {
                bool ext = r.Str("U_CtrlKey") == ControlKeys.External;
                h.Append("<tr><td>").Append(E(r.Str("U_OpNo"))).Append("</td><td>").Append(E(r.Str("U_Descr")))
                 .Append(ext ? "<br><i>Prestataire : " + E(ServiceContext.VendorName(r.Str("U_Vendor"))) + "</i>" : "")
                 .Append("</td><td>").Append(E(r.Str("U_WorkCtr"))).Append("</td><td>").Append(ext ? "" : Q(r.Dbl("U_PlanHrs")))
                 .Append("</td><td></td><td></td><td><span class=\"box\"></span></td></tr>");
            }
            h.Append("</table>");

            // ---- Pièces -----------------------------------------------------
            List<Row> comps = Sql.Rows("SELECT * FROM " + Db.T(Db.OrderComps) + " WHERE \"DocEntry\" = " + orderEntry + " ORDER BY \"LineId\"");
            h.Append("<h2>Pièces prévues</h2><table><tr><th>Article</th><th>Désignation</th><th>Magasin</th><th>Qté prévue</th><th>Qté sortie</th><th>Qté utilisée</th></tr>");
            foreach (Row r in comps)
                h.Append("<tr><td>").Append(E(r.Str("U_ItemCode"))).Append("</td><td>").Append(E(r.Str("U_ItemName"))).Append("</td><td>").Append(E(r.Str("U_Whs")))
                 .Append("</td><td>").Append(Q(r.Dbl("U_Qty"))).Append("</td><td>").Append(r.Dbl("U_IssQty") == 0 ? "" : Q(r.Dbl("U_IssQty"))).Append("</td><td></td></tr>");
            for (int i = 0; i < (comps.Count == 0 ? 3 : 1); i++)
                h.Append("<tr><td class=\"blank\"></td><td></td><td></td><td></td><td></td><td></td></tr>");
            h.Append("</table>");

            // ---- Relevés ----------------------------------------------------
            if (eq != null)
            {
                List<Row> pts = Sql.Rows("SELECT * FROM " + Db.T(Db.EquipPts) + " WHERE \"Code\" = " + Sql.Q(eq.Str("Code")) + " ORDER BY \"LineId\"");
                if (pts.Count > 0)
                {
                    h.Append("<h2>Relevés à noter</h2><table><tr><th>Point</th><th>Description</th><th>Unité</th><th>Limites</th><th>Valeur relevée</th></tr>");
                    foreach (Row p in pts)
                    {
                        string lim = p.Dbl("U_MinVal") != 0 || p.Dbl("U_MaxVal") != 0 ? Q(p.Dbl("U_MinVal")) + " à " + Q(p.Dbl("U_MaxVal")) : "";
                        h.Append("<tr><td>").Append(E(p.Str("U_Point"))).Append("</td><td>").Append(E(p.Str("U_Descr")))
                         .Append(p.Str("U_Counter") == "Y" ? " (compteur)" : "").Append("</td><td>").Append(E(p.Str("U_Unit")))
                         .Append("</td><td>").Append(lim).Append("</td><td></td></tr>");
                    }
                    h.Append("</table>");
                }
            }

            // ---- Compte rendu et signatures ---------------------------------
            h.Append("<h2>Compte rendu : constat, cause, travaux réalisés</h2><div class=\"lines\"><div></div><div></div><div></div><div></div></div>");
            h.Append("<p>Équipement remis en service : <span class=\"box\"></span> oui &nbsp; <span class=\"box\"></span> non &nbsp;&nbsp; ")
             .Append("Début des travaux : le ...../...../........ à .....h..... &nbsp; Fin : le ...../...../........ à .....h.....</p>");
            h.Append("<table class=\"sign\"><tr><th>Technicien (nom, signature)</th><th>Responsable maintenance</th><th>Utilisateur / demandeur</th></tr>")
             .Append("<tr><td></td><td></td><td></td></tr></table>");
            h.Append("</body></html>");
            return h.ToString();
        }

        private static void Line(StringBuilder h, string label, string html)
        {
            h.Append("<tr><td class=\"k\">").Append(E(label)).Append("</td><td>").Append(html).Append("</td></tr>");
        }
    }
}
