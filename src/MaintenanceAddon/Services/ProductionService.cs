using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using SAPbobsCOM;
using MaintenanceAddon.Core;
using MaintenanceAddon.Models;

namespace MaintenanceAddon.Services
{
    /// <summary>Ordre de fabrication SAP (OWOR) vu par la maintenance.</summary>
    internal sealed class ProdOrderInfo
    {
        public int DocEntry;
        public string DocNum = "";
        public string ItemCode = "";
        public string ProdName = "";
        public string Status = "";
        public double PlannedQty;
        public double CompletedQty;
        public double RejectedQty;
        public DateTime? Start;
        public DateTime? Due;
        public DateTime? Closed;
        /// <summary>Ligne de production : celle de l'OF, sinon la ligne par défaut du produit.</summary>
        public string Line = "";
        public string LineName = "";

        /// <summary>Début et fin de la période de production (fin = aujourd'hui si l'OF est ouvert).</summary>
        public DateTime From => Start ?? DateTime.Today;
        public DateTime To => Closed ?? DateTime.Today;

        public string Label()
        {
            return "OF n° " + DocNum + " - " + ProdName + (Line != "" ? " - ligne " + Line + " " + LineName : "");
        }
    }

    /// <summary>
    /// Lien avec la production SAP : la ligne (poste technique « ligne de production »)
    /// porte les machines (équipements) ; l'ordre de fabrication indique sa ligne, ou hérite
    /// de celle de son produit. Interventions et pièces par OF, alertes avant lancement,
    /// compteurs des machines alimentés par les entrées de production.
    /// </summary>
    internal static class ProductionService
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        /// <summary>Expression SQL de la ligne d'un OF (alias w = OWOR, i = OITM du produit).</summary>
        private const string LineExpr = "COALESCE(NULLIF(w.\"U_MNT_PLine\", ''), NULLIF(i.\"U_MNT_PLine\", ''), '')";

        public static ProdOrderInfo Load(int docEntry)
        {
            if (docEntry <= 0)
                return null;
            Row r = Sql.First(
                "SELECT w.\"DocEntry\", w.\"DocNum\", w.\"ItemCode\", ISNULL(w.\"ProdName\", i.\"ItemName\") AS \"PName\", w.\"Status\", " +
                " w.\"PlannedQty\", w.\"CmpltQty\", w.\"RjctQty\", COALESCE(w.\"StartDate\", w.\"PostDate\") AS \"Start\", w.\"DueDate\", w.\"CloseDate\", " +
                LineExpr + " AS \"Line\" FROM \"OWOR\" w LEFT JOIN \"OITM\" i ON i.\"ItemCode\" = w.\"ItemCode\" WHERE w.\"DocEntry\" = " + docEntry);
            if (r == null)
                return null;
            return new ProdOrderInfo
            {
                DocEntry = r.Int("DocEntry"),
                DocNum = r.Str("DocNum"),
                ItemCode = r.Str("ItemCode"),
                ProdName = r.Str("PName"),
                Status = r.Str("Status"),
                PlannedQty = r.Dbl("PlannedQty"),
                CompletedQty = r.Dbl("CmpltQty"),
                RejectedQty = r.Dbl("RjctQty"),
                Start = r.Date("Start"),
                Due = r.Date("DueDate"),
                Closed = r.Str("Status") == ProdStatus.Closed || r.Str("Status") == ProdStatus.Cancelled ? r.Date("CloseDate") ?? DateTime.Today : (DateTime?)null,
                Line = r.Str("Line"),
                LineName = NotificationService.NameOf(Db.FuncLoc, r.Str("Line"))
            };
        }

        public static bool IsLine(string code)
        {
            return Sql.Exists("SELECT 1 FROM " + Db.T(Db.FuncLoc) + " WHERE \"Code\" = " + Sql.Q(code ?? "") + " AND \"U_IsLine\" = 'Y'");
        }

        /// <summary>Ligne de production d'un emplacement : lui-même ou le premier poste supérieur marqué « ligne ».</summary>
        public static string LineOf(string funcLoc)
        {
            string fl = funcLoc ?? "";
            for (int i = 0; i < 30 && fl != ""; i++)
            {
                Row r = Sql.First("SELECT \"U_IsLine\", \"U_Parent\" FROM " + Db.T(Db.FuncLoc) + " WHERE \"Code\" = " + Sql.Q(fl));
                if (r == null)
                    return "";
                if (r.Str("U_IsLine") == "Y")
                    return fl;
                fl = r.Str("U_Parent");
            }
            return "";
        }

        public static string LineOfEquipment(string equip)
        {
            EquipmentInfo eq = EquipmentInfo.Load(equip);
            return eq == null ? "" : LineOf(eq.FuncLoc);
        }

        /// <summary>Machines de la ligne : équipements installés sur la ligne ou ses sous-postes (hors rebut).</summary>
        public static List<string> Machines(string line)
        {
            if (string.IsNullOrEmpty(line))
                return new List<string>();
            return Sql.Rows(
                "WITH t AS (SELECT \"Code\" FROM " + Db.T(Db.FuncLoc) + " WHERE \"Code\" = " + Sql.Q(line) +
                " UNION ALL SELECT c.\"Code\" FROM " + Db.T(Db.FuncLoc) + " c JOIN t ON c.\"U_Parent\" = t.\"Code\") " +
                "SELECT e.\"Code\" FROM " + Db.T(Db.Equip) + " e WHERE e.\"U_FuncLoc\" IN (SELECT \"Code\" FROM t) AND ISNULL(e.\"U_Status\", 'A') <> 'S' " +
                "ORDER BY e.\"Code\"").Select(r => r.Str("Code")).ToList();
        }

        private static string InList(IEnumerable<string> codes)
        {
            List<string> list = codes.ToList();
            return list.Count == 0 ? "(N'\u0001')" : "(" + string.Join(", ", list.Select(Sql.Q)) + ")";
        }

        /// <summary>OF lancé en cours sur la ligne de l'équipement (le plus récent), ou 0.</summary>
        public static int CurrentOrderForEquipment(string equip, DateTime date)
        {
            string line = LineOfEquipment(equip);
            if (line == "")
                return 0;
            return (int)Sql.ScalarDbl(
                "SELECT TOP 1 w.\"DocEntry\" FROM \"OWOR\" w LEFT JOIN \"OITM\" i ON i.\"ItemCode\" = w.\"ItemCode\" " +
                "WHERE w.\"Status\" = 'R' AND " + LineExpr + " = " + Sql.Q(line) +
                " AND COALESCE(w.\"StartDate\", w.\"PostDate\") <= " + Sql.D(date) +
                " ORDER BY COALESCE(w.\"StartDate\", w.\"PostDate\") DESC, w.\"DocEntry\" DESC");
        }

        /// <summary>
        /// Points à vérifier avant de lancer une production sur la ligne : machines hors service
        /// ou chez un prestataire, pannes en cours, préventifs en retard, pièces critiques manquantes.
        /// </summary>
        public static List<string> ReleaseWarnings(string line)
        {
            var w = new List<string>();
            List<string> machines = Machines(line);
            if (machines.Count == 0)
                return w;
            string inM = InList(machines);

            foreach (Row r in Sql.Rows("SELECT \"Code\", \"Name\", \"U_Status\" FROM " + Db.T(Db.Equip) + " WHERE \"Code\" IN " + inM +
                                       " AND \"U_Status\" IN ('I', 'R') ORDER BY \"Code\""))
                w.Add(r.Str("Code") + " " + r.Str("Name") + " : " + EquipStatus.List.Caption(r.Str("U_Status")).ToLowerInvariant() +
                      (r.Str("U_Status") == EquipStatus.AtVendor ? " (" + ShipmentService.Describe(ShipmentService.OpenShipment(r.Str("Code"))) + ")" : ""));

            foreach (Row r in Sql.Rows("SELECT n.\"DocNum\", n.\"U_Equip\", n.\"U_Subject\", n.\"U_MalfStD\" FROM " + Db.T(Db.Notif) + " n " +
                                       "WHERE n.\"U_Equip\" IN " + inM + " AND n.\"U_Breakdwn\" = 'Y' AND n.\"U_MalfEnD\" IS NULL AND n.\"U_Status\" <> 'NOCO' ORDER BY n.\"DocNum\""))
                w.Add(r.Str("U_Equip") + " : panne en cours depuis le " + r.Date("U_MalfStD")?.ToString("dd/MM/yyyy") + " (avis " + r.Str("DocNum") + " - " + r.Str("U_Subject") + ")");

            var set = new HashSet<string>(machines);
            foreach (PlanDue p in PlanService.Overview(DateTime.Today).Where(p => set.Contains(p.Equip) && p.IsDue && p.OpenOrder == 0 &&
                                                                                 p.DueDate.HasValue && p.DueDate.Value < DateTime.Today))
                w.Add(p.Equip + " : entretien préventif en retard, " + p.PlanName + " (échéance du " + p.DueDate.Value.ToString("dd/MM/yyyy") + ")");

            foreach (Row r in Sql.Rows("SELECT p.\"Code\", p.\"U_ItemCode\", ISNULL(i.\"ItemName\", p.\"U_ItemName\") AS \"Name\", p.\"U_Qty\", " +
                                       "ISNULL(i.\"OnHand\", 0) - ISNULL(i.\"IsCommited\", 0) AS \"Avail\" FROM " + Db.T(Db.EquipParts) + " p " +
                                       "LEFT JOIN \"OITM\" i ON i.\"ItemCode\" = p.\"U_ItemCode\" WHERE p.\"Code\" IN " + inM +
                                       " AND ISNULL(i.\"InvntItem\", 'N') = 'Y' AND ISNULL(i.\"OnHand\", 0) - ISNULL(i.\"IsCommited\", 0) < p.\"U_Qty\" ORDER BY p.\"Code\""))
                w.Add(r.Str("Code") + " : pièce de rechange " + r.Str("U_ItemCode") + " " + r.Str("Name") + " insuffisante en stock (" +
                      r.Dbl("Avail").ToString("N0", Fr) + " disponible(s) pour " + r.Dbl("U_Qty").ToString("N0", Fr) + " montée(s))");
            return w;
        }

        /// <summary>Condition SQL des documents de maintenance de l'OF : rattachés, ou sur une machine de la ligne pendant la production.</summary>
        private static string Scope(string alias, string dateCol, ProdOrderInfo of, List<string> machines)
        {
            return "(" + alias + ".\"U_ProdOrd\" = " + of.DocEntry +
                   " OR (ISNULL(" + alias + ".\"U_ProdOrd\", 0) = 0 AND " + alias + ".\"U_Equip\" IN " + InList(machines) +
                   " AND " + alias + "." + dateCol + " BETWEEN " + Sql.D(of.From) + " AND " + Sql.D(of.To) + "))";
        }

        /// <summary>Avis et ordres de maintenance de l'OF (onglet de suivi).</summary>
        public static string InterventionsSql(ProdOrderInfo of)
        {
            List<string> m = Machines(of.Line);
            string link = " CASE WHEN {0}.\"U_ProdOrd\" = " + of.DocEntry + " THEN N'Rattaché à l''OF' ELSE N'Sur la ligne pendant l''OF' END";
            return "SELECT N'" + Obj.Notif + "' AS \"KeyObj\", n.\"DocEntry\" AS \"Key\", n.\"U_RepDate\" AS \"Date\", N'Avis' AS \"Nature\", n.\"DocNum\" AS \"Num\", " +
                   "n.\"U_Equip\" AS \"Equip\", n.\"U_Subject\" AS \"Objet\", " + NotifStatus.List.SqlCase("n.\"U_Status\"") + " AS \"Statut\", " +
                   "CAST(CASE WHEN n.\"U_LineStop\" = 'Y' THEN " + ReportService.Downtime("n") + " ELSE 0 END AS DECIMAL(19, 2)) AS \"ArretProd\", " +
                   "ISNULL(n.\"U_LostQty\", 0) AS \"QtePerdue\", CAST(0 AS DECIMAL(19, 2)) AS \"CoutMaint\", " + string.Format(link, "n") + " AS \"Lien\" " +
                   "FROM " + Db.T(Db.Notif) + " n WHERE " + Scope("n", "\"U_RepDate\"", of, m) +
                   " UNION ALL SELECT N'" + Obj.Order + "', o.\"DocEntry\", o.\"U_StartDt\", N'Ordre', o.\"DocNum\", o.\"U_Equip\", o.\"U_Subject\", " +
                   OrderStatus.List.SqlCase("o.\"U_Status\"") + ", 0, 0, (o.\"U_AcLab\" + o.\"U_AcMat\" + o.\"U_AcExt\"), " + string.Format(link, "o") + " " +
                   "FROM " + Db.T(Db.Order) + " o WHERE o.\"U_Status\" <> 'CANC' AND " + Scope("o", "\"U_StartDt\"", of, m) +
                   " ORDER BY \"Date\" DESC, \"Nature\", \"Num\" DESC";
        }

        /// <summary>Pièces consommées par les ordres de maintenance de l'OF (sorties nettes des retours).</summary>
        public static string PartsSql(ProdOrderInfo of)
        {
            string orders = "(SELECT o.\"DocEntry\" FROM " + Db.T(Db.Order) + " o WHERE o.\"U_Status\" <> 'CANC' AND " +
                            Scope("o", "\"U_StartDt\"", of, Machines(of.Line)) + ")";
            return "SELECT x.\"Article\", MAX(x.\"Desig\") AS \"Desig\", SUM(x.\"Qte\") AS \"Qte\", SUM(x.\"Valeur\") AS \"Valeur\" FROM (" +
                   "SELECT l.\"ItemCode\" AS \"Article\", l.\"Dscription\" AS \"Desig\", l.\"Quantity\" AS \"Qte\", l.\"StockSum\" AS \"Valeur\" " +
                   "FROM \"IGE1\" l JOIN \"OIGE\" h ON h.\"DocEntry\" = l.\"DocEntry\" WHERE h.\"CANCELED\" = 'N' AND l.\"" + DocFields.Order + "\" IN " + orders +
                   " UNION ALL SELECT l.\"ItemCode\", l.\"Dscription\", -l.\"Quantity\", -l.\"LineTotal\" " +
                   "FROM \"IGN1\" l JOIN \"OIGN\" h ON h.\"DocEntry\" = l.\"DocEntry\" WHERE h.\"CANCELED\" = 'N' AND l.\"" + DocFields.Order + "\" IN " + orders +
                   ") x GROUP BY x.\"Article\" HAVING SUM(x.\"Qte\") <> 0 ORDER BY SUM(x.\"Valeur\") DESC";
        }

        /// <summary>Pièces de rechange des machines de la ligne, les manquantes en tête.</summary>
        public static string CriticalPartsSql(string line)
        {
            return "SELECT p.\"Code\" AS \"Equip\", p.\"U_ItemCode\" AS \"Article\", ISNULL(i.\"ItemName\", p.\"U_ItemName\") AS \"Desig\", p.\"U_Qty\" AS \"QteMont\", " +
                   "ISNULL(i.\"OnHand\", 0) - ISNULL(i.\"IsCommited\", 0) AS \"Stock\", ISNULL(i.\"MinLevel\", 0) AS \"Mini\", " +
                   "CASE WHEN ISNULL(i.\"InvntItem\", 'N') <> 'Y' THEN N'Non stocké (achat)' " +
                   " WHEN ISNULL(i.\"OnHand\", 0) - ISNULL(i.\"IsCommited\", 0) < p.\"U_Qty\" THEN N'Stock < quantité montée' " +
                   " WHEN ISNULL(i.\"MinLevel\", 0) > 0 AND ISNULL(i.\"OnHand\", 0) - ISNULL(i.\"IsCommited\", 0) < i.\"MinLevel\" THEN N'Sous le stock mini' ELSE N'' END AS \"Alerte\" " +
                   "FROM " + Db.T(Db.EquipParts) + " p LEFT JOIN \"OITM\" i ON i.\"ItemCode\" = p.\"U_ItemCode\" " +
                   "WHERE p.\"Code\" IN " + InList(Machines(line)) + " AND ISNULL(p.\"U_ItemCode\", '') <> '' " +
                   "ORDER BY CASE WHEN ISNULL(i.\"OnHand\", 0) - ISNULL(i.\"IsCommited\", 0) < p.\"U_Qty\" THEN 0 ELSE 1 END, p.\"Code\", p.\"U_ItemCode\"";
        }

        /// <summary>Synthèse de l'OF : nombre d'avis et d'ordres, arrêt de production, quantité perdue, coût de maintenance.</summary>
        public static string Summary(ProdOrderInfo of)
        {
            List<Row> rows = Sql.Rows(InterventionsSql(of));
            int notifs = rows.Count(r => r.Str("Nature") == "Avis"), orders = rows.Count - notifs;
            return notifs + " avis, " + orders + " ordre(s) de maintenance  |  arrêt de production " +
                   rows.Sum(r => r.Dbl("ArretProd")).ToString("N1", Fr) + " h  |  quantité perdue " + rows.Sum(r => r.Dbl("QtePerdue")).ToString("N0", Fr) +
                   "  |  coût de maintenance " + Sql.Amount(rows.Sum(r => r.Dbl("CoutMaint")));
        }

        // =====================================================================
        // Compteurs alimentés par la production
        // =====================================================================

        /// <summary>
        /// Ajoute les quantités des entrées de production (produit de l'OF) aux compteurs
        /// « comptés par la production » des machines de la ligne. Les entrées antérieures à
        /// la première synchronisation ne sont pas comptées. Renvoie le nombre de relevés créés.
        /// </summary>
        public static int SyncCounters()
        {
            double mark = Sql.ScalarDbl("SELECT ISNULL(\"U_ProdSync\", 0) FROM " + Db.T(Db.Setup) + " WHERE \"Code\" = " + Sql.Q(Db.SetupCode));
            if (mark <= 0)
            {
                // Première fois : on part d'aujourd'hui (les relevés de départ se saisissent à la main)
                SetMark(Math.Max(1, Sql.ScalarDbl("SELECT ISNULL(MAX(\"DocEntry\"), 0) FROM \"OIGN\"")));
                return 0;
            }

            List<Row> receipts = Sql.Rows(
                "SELECT h.\"DocEntry\", h.\"DocNum\", h.\"DocDate\", l.\"LineNum\", COALESCE(NULLIF(l.\"InvQty\", 0), l.\"Quantity\") AS \"Qty\", " +
                " w.\"DocNum\" AS \"OfNum\", " + LineExpr + " AS \"Line\" " +
                "FROM \"OIGN\" h JOIN \"IGN1\" l ON l.\"DocEntry\" = h.\"DocEntry\" " +
                "JOIN \"OWOR\" w ON w.\"DocEntry\" = l.\"BaseEntry\" AND l.\"BaseType\" = 202 LEFT JOIN \"OITM\" i ON i.\"ItemCode\" = w.\"ItemCode\" " +
                "WHERE h.\"DocEntry\" > " + Sql.N(mark) + " AND h.\"CANCELED\" = 'N' AND l.\"ItemCode\" = w.\"ItemCode\" " +
                "ORDER BY h.\"DocEntry\", l.\"LineNum\"");
            int count = 0;
            double last = mark;
            foreach (Row r in receipts)
            {
                last = Math.Max(last, r.Dbl("DocEntry"));
                if (r.Str("Line") == "" || r.Dbl("Qty") <= 0)
                    continue;
                long key = (long)r.Dbl("DocEntry") * 1000 + r.Int("LineNum");
                foreach (Row p in Sql.Rows("SELECT \"Code\", \"U_Point\" FROM " + Db.T(Db.EquipPts) + " WHERE \"Code\" IN " + InList(Machines(r.Str("Line"))) +
                                           " AND \"U_Counter\" = 'Y' AND \"U_ProdCnt\" = 'Y'"))
                {
                    string equip = p.Str("Code"), point = p.Str("U_Point");
                    if (Sql.Exists("SELECT 1 FROM " + Db.T(Db.MeasDoc) + " WHERE \"U_Equip\" = " + Sql.Q(equip) + " AND \"U_Point\" = " + Sql.Q(point) +
                                   " AND \"U_SrcDoc\" = " + key))
                        continue;
                    try
                    {
                        Row lastReading = MeasurementService.LastReading(equip, point);
                        DateTime date = r.Date("DocDate") ?? DateTime.Today;
                        int time = 0;
                        DateTime? lastDate = lastReading?.Date("U_MDate");
                        if (lastDate.HasValue && lastDate.Value >= date)
                        {
                            date = lastDate.Value;
                            time = lastReading.Int("U_MTime");
                        }
                        if (date > DateTime.Today)
                            date = DateTime.Today;
                        double value = (lastReading?.Dbl("U_Value") ?? 0) + r.Dbl("Qty");
                        MeasurementService.RecordData(equip, point, date, time, value,
                            "Production : OF n° " + r.Str("OfNum") + ", entrée n° " + r.Str("DocNum"), key);
                        count++;
                    }
                    catch (Exception ex)
                    {
                        Program.Log("Compteur de production " + equip + "/" + point + " : " + ex.Message);
                    }
                }
            }
            if (last > mark)
                SetMark(last);
            return count;
        }

        private static void SetMark(double docEntry)
        {
            UserTable table = DiCompany.Instance.UserTables.Item(Db.Setup);
            try
            {
                if (!table.GetByKey(Db.SetupCode))
                    return;
                table.UserFields.Fields.Item("U_ProdSync").Value = (int)docEntry;
                DiCompany.ThrowIfError(table.Update(), "Suivi des entrées de production");
            }
            finally
            {
                Marshal.ReleaseComObject(table);
            }
        }
    }
}
