using System;
using System.Collections.Generic;
using System.Globalization;
using MaintenanceAddon.Core;
using MaintenanceAddon.Models;

namespace MaintenanceAddon.Services
{
    internal sealed class ReportFilter
    {
        public DateTime From;
        public DateTime To;
        public string Equip;
        public string Status;
        public string Type;
        public string WorkCtr;
    }

    internal sealed class ReportView
    {
        public string Code;
        public string Title;
        public Func<ReportFilter, string> Sql;
        public CodeList Statuses;
        public CodeList Types;
        public bool UsesDates = true;
        /// <summary>Objet ouvert par la colonne « Num » (la clé est dans la colonne cachée « Key »).</summary>
        public string NumObject;
        /// <summary>Colonnes dont la valeur est la clé d'un objet de l'add-on.</summary>
        public Dictionary<string, string> Links = new Dictionary<string, string>();
        /// <summary>Colonnes totalisées sous la grille.</summary>
        public string[] Totals = new string[0];
        /// <summary>Le double-clic ouvre l'objet des colonnes cachées KeyObj / Key.</summary>
        public bool HasKeyObj;
    }

    /// <summary>
    /// Rapports de maintenance (équivalents IW39, IW29, IH01, MCI*, IW47, IK17, IP24...).
    /// Requêtes SQL Server ; colonnes courtes, libellés dans <see cref="Captions"/>.
    /// </summary>
    internal static class ReportService
    {
        public static readonly Dictionary<string, string> Captions = new Dictionary<string, string>
        {
            { "Num", "N°" }, { "Type", "Type" }, { "Statut", "Statut" }, { "Equip", "Équipement" }, { "EqName", "Désignation équipement" },
            { "Objet", "Description" }, { "Prio", "Priorité" }, { "Debut", "Début prévu" }, { "Fin", "Fin prévue" }, { "Poste", "Poste de travail" },
            { "Avis", "Avis" }, { "Ordre", "Ordre" }, { "Plan", "Plan" }, { "Prevu", "Coût prévu" }, { "Reel", "Coût réel" }, { "Ecart", "Écart" },
            { "Retard", "En retard" }, { "JRetard", "Jours de retard" }, { "Date", "Date" }, { "FinSouh", "Fin souhaitée" }, { "Arret", "Arrêt" },
            { "Panne", "Durée panne (h)" }, { "Dommage", "Dommage" }, { "Cause", "Cause" }, { "Element", "Élément" }, { "Desig", "Désignation" },
            { "Nature", "Nature" }, { "Chemin", "Chemin" }, { "Categ", "Catégorie" }, { "Crit", "Criticité" }, { "NbOrd", "Nb ordres" },
            { "NbPan", "Nb pannes" }, { "ArretH", "Arrêt total (h)" }, { "MTTR", "MTTR (h)" }, { "MTBF", "MTBF (h)" }, { "Dispo", "Disponibilité %" },
            { "CoutMO", "Main-d'oeuvre" }, { "CoutMat", "Pièces" }, { "CoutExt", "Prestations" }, { "CoutTot", "Coût total" }, { "Mois", "Mois" },
            { "Op", "Opération" }, { "Salarie", "Salarié" }, { "Heures", "Heures" }, { "Montant", "Montant" }, { "Finale", "Finale" },
            { "Ecriture", "N° écriture" }, { "Comment", "Commentaire" }, { "Heure", "Heure" }, { "Point", "Point de mesure" }, { "Valeur", "Valeur" },
            { "Unite", "Unité" }, { "Diff", "Écart / précédent" }, { "Echeance", "Échéance" }, { "EchCpt", "Échéance compteur" }, { "Realise", "Réalisé le" },
            { "Mvt", "Mouvement" }, { "Doc", "N° document" }, { "Article", "Article" }, { "Qte", "Quantité" }, { "Mag", "Magasin" }, { "Util", "Utilisateur" },
            { "Etat", "État" }, { "Contrat", "Contrat" }, { "Prest", "Prestataire" }, { "CtrDeb", "Début" }, { "CtrFin", "Fin" },
            { "JRest", "Jours restants" }, { "Preavis", "Résilier avant le" }, { "MontAn", "Montant annuel" }, { "Prorata", "Prévu à date" },
            { "Facture", "Facturé" }, { "NbEq", "Nb équipements" }, { "Envoi", "Envoyé le" }, { "RetPrev", "Retour prévu" },
            { "RetReel", "Revenu le" }, { "Jours", "Jours" }, { "Motif", "Motif" }, { "Garant", "Garant" }, { "FinGar", "Fin de garantie" },
            { "Garantie", "Garantie" }, { "Couv", "Couverture" }, { "Delai", "Délai interv. (h)" }
        };

        public static readonly List<ReportView> Views = new List<ReportView>();

        static ReportService()
        {
            Views.Add(new ReportView
            {
                Code = "ORD", Title = "Liste des ordres", Sql = OrdersSql, Statuses = OrderStatus.List, Types = OrderTypes.List,
                NumObject = Obj.Order, Links = { { "Equip", Obj.Equip }, { "Avis", Obj.Notif }, { "Plan", Obj.Plan } },
                Totals = new[] { "Prevu", "Reel" }
            });
            Views.Add(new ReportView
            {
                Code = "BKL", Title = "Carnet d'ordres en retard", Sql = BacklogSql, UsesDates = false, Types = OrderTypes.List,
                NumObject = Obj.Order, Links = { { "Equip", Obj.Equip } }, Totals = new[] { "Prevu" }
            });
            Views.Add(new ReportView
            {
                Code = "NOT", Title = "Liste des avis", Sql = NotificationsSql, Statuses = NotifStatus.List, Types = NotifTypes.List,
                NumObject = Obj.Notif, Links = { { "Equip", Obj.Equip }, { "Ordre", Obj.Order } }, Totals = new[] { "Panne" }
            });
            Views.Add(new ReportView { Code = "STR", Title = "Structure technique", Sql = StructureSql, UsesDates = false, HasKeyObj = true });
            Views.Add(new ReportView
            {
                Code = "CTR", Title = "Contrats de maintenance (échéances, facturé)", Sql = ContractsSql, UsesDates = false, HasKeyObj = true,
                Totals = new[] { "MontAn", "Prorata", "Facture" }
            });
            Views.Add(new ReportView
            {
                Code = "SHP", Title = "Équipements envoyés chez les prestataires", Sql = ShipmentsSql, Statuses = ShipStatus.List,
                NumObject = Obj.Order, Links = { { "Equip", Obj.Equip } }
            });
            Views.Add(new ReportView
            {
                Code = "WAR", Title = "Garanties des équipements", Sql = WarrantiesSql, Links = { { "Equip", Obj.Equip } }
            });
            Views.Add(new ReportView
            {
                Code = "KPI", Title = "Indicateurs équipements (MTBF / MTTR / coûts)", Sql = KpiSql,
                Links = { { "Equip", Obj.Equip } }, Totals = new[] { "NbOrd", "NbPan", "ArretH", "CoutMO", "CoutMat", "CoutExt", "CoutTot" }
            });
            Views.Add(new ReportView
            {
                Code = "CST", Title = "Coûts par mois et type d'ordre", Sql = CostsSql, Types = OrderTypes.List,
                Totals = new[] { "NbOrd", "Prevu", "CoutMO", "CoutMat", "CoutExt", "CoutTot", "Ecart" }
            });
            Views.Add(new ReportView
            {
                Code = "CNF", Title = "Confirmations de temps", Sql = ConfirmationsSql,
                NumObject = Obj.Order, Links = { { "Equip", Obj.Equip } }, Totals = new[] { "Heures", "Montant" }
            });
            Views.Add(new ReportView
            {
                Code = "MAT", Title = "Consommations de pièces", Sql = MaterialsSql,
                NumObject = Obj.Order, Links = { { "Equip", Obj.Equip } }, Totals = new[] { "Valeur" }
            });
            Views.Add(new ReportView
            {
                Code = "MEA", Title = "Historique des relevés", Sql = MeasurementsSql, Links = { { "Equip", Obj.Equip }, { "Avis", Obj.Notif } }
            });
            Views.Add(new ReportView
            {
                Code = "PLN", Title = "Historique des appels de plans", Sql = CallsSql, Statuses = CallStatus.List,
                NumObject = Obj.Order, Links = { { "Plan", Obj.Plan }, { "Equip", Obj.Equip } }
            });
        }

        public static ReportView View(string code)
        {
            foreach (ReportView v in Views)
                if (v.Code == code)
                    return v;
            return Views[0];
        }

        // ---------------------------------------------------------------------

        private static string Between(string column, ReportFilter f)
        {
            return " AND " + column + " BETWEEN " + Sql.D(f.From) + " AND " + Sql.D(f.To);
        }

        private static string Eq(string column, string value)
        {
            return string.IsNullOrEmpty(value) ? "" : " AND " + column + " = " + Sql.Q(value);
        }

        /// <summary>Date + heure SAP (HHMM) en datetime SQL.</summary>
        private static string Ts(string dateCol, string timeCol)
        {
            return "DATEADD(minute, ISNULL(" + timeCol + ", 0) / 100 * 60 + ISNULL(" + timeCol + ", 0) % 100, " + dateCol + ")";
        }

        /// <summary>Durée d'arrêt (h) d'un avis de panne ; une panne en cours compte jusqu'à maintenant.</summary>
        private static string Downtime(string n)
        {
            return "CASE WHEN " + n + ".\"U_Breakdwn\" = 'Y' AND " + n + ".\"U_MalfStD\" IS NOT NULL THEN " +
                   "DATEDIFF(minute, " + Ts(n + ".\"U_MalfStD\"", n + ".\"U_MalfStT\"") + ", " +
                   "ISNULL(" + Ts(n + ".\"U_MalfEnD\"", n + ".\"U_MalfEnT\"") + ", GETDATE())) / 60.0 ELSE 0 END";
        }

        private const string Planned = "(o.\"U_PlLab\" + o.\"U_PlMat\" + o.\"U_PlExt\")";
        private const string Actual = "(o.\"U_AcLab\" + o.\"U_AcMat\" + o.\"U_AcExt\")";

        private static string OrdersSql(ReportFilter f)
        {
            return "SELECT o.\"DocEntry\" AS \"Key\", o.\"DocNum\" AS \"Num\", " + OrderTypes.List.SqlCase("o.\"U_OrdType\"") + " AS \"Type\", " +
                   OrderStatus.List.SqlCase("o.\"U_Status\"") + " AS \"Statut\", o.\"U_Equip\" AS \"Equip\", e.\"Name\" AS \"EqName\", " +
                   "o.\"U_Subject\" AS \"Objet\", " + Priorities.List.SqlCase("o.\"U_Priority\"") + " AS \"Prio\", " +
                   "o.\"U_StartDt\" AS \"Debut\", o.\"U_EndDt\" AS \"Fin\", o.\"U_WorkCtr\" AS \"Poste\", " +
                   "o.\"U_NotifNo\" AS \"Avis\", o.\"U_PlanCode\" AS \"Plan\", " + Planned + " AS \"Prevu\", " + Actual + " AS \"Reel\", " +
                   "CASE WHEN o.\"U_Status\" IN ('CRTD', 'REL') AND o.\"U_EndDt\" < CAST(GETDATE() AS DATE) THEN N'Oui' ELSE N'' END AS \"Retard\" " +
                   "FROM " + Db.T(Db.Order) + " o LEFT JOIN " + Db.T(Db.Equip) + " e ON e.\"Code\" = o.\"U_Equip\" " +
                   "WHERE 1 = 1" + Between("o.\"U_StartDt\"", f) + Eq("o.\"U_Status\"", f.Status) + Eq("o.\"U_OrdType\"", f.Type) +
                   Eq("o.\"U_Equip\"", f.Equip) + Eq("o.\"U_WorkCtr\"", f.WorkCtr) +
                   " ORDER BY o.\"U_StartDt\" DESC, o.\"DocNum\" DESC";
        }

        private static string BacklogSql(ReportFilter f)
        {
            return "SELECT o.\"DocEntry\" AS \"Key\", o.\"DocNum\" AS \"Num\", " + OrderTypes.List.SqlCase("o.\"U_OrdType\"") + " AS \"Type\", " +
                   OrderStatus.List.SqlCase("o.\"U_Status\"") + " AS \"Statut\", o.\"U_Equip\" AS \"Equip\", e.\"Name\" AS \"EqName\", " +
                   "o.\"U_Subject\" AS \"Objet\", " + Priorities.List.SqlCase("o.\"U_Priority\"") + " AS \"Prio\", " +
                   "o.\"U_EndDt\" AS \"Fin\", DATEDIFF(day, o.\"U_EndDt\", GETDATE()) AS \"JRetard\", o.\"U_WorkCtr\" AS \"Poste\", " +
                   Planned + " AS \"Prevu\" " +
                   "FROM " + Db.T(Db.Order) + " o LEFT JOIN " + Db.T(Db.Equip) + " e ON e.\"Code\" = o.\"U_Equip\" " +
                   "WHERE o.\"U_Status\" IN ('CRTD', 'REL') AND o.\"U_EndDt\" < CAST(GETDATE() AS DATE)" +
                   Eq("o.\"U_OrdType\"", f.Type) + Eq("o.\"U_Equip\"", f.Equip) + Eq("o.\"U_WorkCtr\"", f.WorkCtr) +
                   " ORDER BY o.\"U_Priority\", o.\"U_EndDt\"";
        }

        private static string NotificationsSql(ReportFilter f)
        {
            return "SELECT n.\"DocEntry\" AS \"Key\", n.\"DocNum\" AS \"Num\", " + NotifTypes.List.SqlCase("n.\"U_Type\"") + " AS \"Type\", " +
                   NotifStatus.List.SqlCase("n.\"U_Status\"") + " AS \"Statut\", n.\"U_Equip\" AS \"Equip\", e.\"Name\" AS \"EqName\", " +
                   "n.\"U_Subject\" AS \"Objet\", " + Priorities.List.SqlCase("n.\"U_Priority\"") + " AS \"Prio\", " +
                   "n.\"U_RepDate\" AS \"Date\", n.\"U_ReqEnd\" AS \"FinSouh\", CASE WHEN n.\"U_Breakdwn\" = 'Y' THEN N'Oui' ELSE N'' END AS \"Arret\", " +
                   "CAST(" + Downtime("n") + " AS DECIMAL(19, 2)) AS \"Panne\", " +
                   "ISNULL(d.\"Name\", n.\"U_Damage\") AS \"Dommage\", ISNULL(c.\"Name\", n.\"U_Cause\") AS \"Cause\", n.\"U_OrderNo\" AS \"Ordre\" " +
                   "FROM " + Db.T(Db.Notif) + " n LEFT JOIN " + Db.T(Db.Equip) + " e ON e.\"Code\" = n.\"U_Equip\" " +
                   "LEFT JOIN " + Db.T(Db.Catalog) + " d ON d.\"Code\" = n.\"U_Damage\" LEFT JOIN " + Db.T(Db.Catalog) + " c ON c.\"Code\" = n.\"U_Cause\" " +
                   "WHERE 1 = 1" + Between("n.\"U_RepDate\"", f) + Eq("n.\"U_Status\"", f.Status) + Eq("n.\"U_Type\"", f.Type) +
                   Eq("n.\"U_Equip\"", f.Equip) + Eq("n.\"U_WorkCtr\"", f.WorkCtr) +
                   " ORDER BY n.\"U_RepDate\" DESC, n.\"DocNum\" DESC";
        }

        private static string StructureSql(ReportFilter f)
        {
            return "WITH fl AS (" +
                   " SELECT \"Code\", \"Name\", CAST(\"Code\" AS NVARCHAR(2000)) AS \"Path\", 0 AS \"Lvl\" FROM " + Db.T(Db.FuncLoc) +
                   " WHERE ISNULL(\"U_Parent\", '') = ''" +
                   " UNION ALL SELECT c.\"Code\", c.\"Name\", CAST(p.\"Path\" + N' > ' + c.\"Code\" AS NVARCHAR(2000)), p.\"Lvl\" + 1" +
                   " FROM " + Db.T(Db.FuncLoc) + " c JOIN fl p ON c.\"U_Parent\" = p.\"Code\" WHERE p.\"Lvl\" < 20), " +
                   "eq AS (" +
                   " SELECT e.\"Code\", e.\"Name\", CAST(ISNULL(fl.\"Path\" + N' > ', N'') + e.\"Code\" AS NVARCHAR(2000)) AS \"Path\"," +
                   " ISNULL(fl.\"Lvl\" + 1, 0) AS \"Lvl\", e.\"U_Status\" AS \"St\" FROM " + Db.T(Db.Equip) + " e LEFT JOIN fl ON fl.\"Code\" = e.\"U_FuncLoc\"" +
                   " WHERE ISNULL(e.\"U_Parent\", '') = ''" +
                   " UNION ALL SELECT c.\"Code\", c.\"Name\", CAST(p.\"Path\" + N' > ' + c.\"Code\" AS NVARCHAR(2000)), p.\"Lvl\" + 1, c.\"U_Status\"" +
                   " FROM " + Db.T(Db.Equip) + " c JOIN eq p ON c.\"U_Parent\" = p.\"Code\" WHERE p.\"Lvl\" < 20) " +
                   "SELECT \"Code\" AS \"Key\", N'" + Obj.FuncLoc + "' AS \"KeyObj\", REPLICATE(N'      ', \"Lvl\") + \"Code\" AS \"Element\", \"Name\" AS \"Desig\"," +
                   " N'Poste technique' AS \"Nature\", N'' AS \"Etat\", \"Path\" AS \"Chemin\" FROM fl " +
                   "UNION ALL SELECT \"Code\", N'" + Obj.Equip + "', REPLICATE(N'      ', \"Lvl\") + \"Code\", \"Name\", N'Équipement', " +
                   EquipStatus.List.SqlCase("\"St\"") + ", \"Path\" FROM eq " +
                   "ORDER BY \"Chemin\"";
        }

        /// <summary>Contrats : échéance, date limite de résiliation, prévu au prorata, facturé (factures − avoirs).</summary>
        private static string ContractsSql(ReportFilter f)
        {
            string invoiced =
                "ISNULL((SELECT SUM(l.\"LineTotal\") FROM \"PCH1\" l JOIN \"OPCH\" h ON h.\"DocEntry\" = l.\"DocEntry\" WHERE h.\"CANCELED\" = 'N' AND l.\"" + DocFields.Contract + "\" = c.\"Code\"), 0) - " +
                "ISNULL((SELECT SUM(l.\"LineTotal\") FROM \"RPC1\" l JOIN \"ORPC\" h ON h.\"DocEntry\" = l.\"DocEntry\" WHERE h.\"CANCELED\" = 'N' AND l.\"" + DocFields.Contract + "\" = c.\"Code\"), 0)";
            string upTo = "CASE WHEN c.\"U_EndDt\" < CAST(GETDATE() AS DATE) THEN c.\"U_EndDt\" ELSE CAST(GETDATE() AS DATE) END";
            return "SELECT c.\"Code\" AS \"Key\", N'" + Obj.Contract + "' AS \"KeyObj\", c.\"Code\" AS \"Contrat\", c.\"Name\" AS \"Desig\", " +
                   "ISNULL(b.\"CardName\", c.\"U_Vendor\") AS \"Prest\", " + ContractTypes.List.SqlCase("c.\"U_Type\"") + " AS \"Type\", " +
                   "c.\"U_StartDt\" AS \"CtrDeb\", c.\"U_EndDt\" AS \"CtrFin\", DATEDIFF(day, GETDATE(), c.\"U_EndDt\") AS \"JRest\", " +
                   "DATEADD(day, -ISNULL(c.\"U_Notice\", 0), c.\"U_EndDt\") AS \"Preavis\", c.\"U_RespHrs\" AS \"Delai\", c.\"U_Amount\" AS \"MontAn\", " +
                   "CAST(CASE WHEN c.\"U_StartDt\" IS NULL OR c.\"U_StartDt\" > GETDATE() THEN 0 ELSE c.\"U_Amount\" * (DATEDIFF(day, c.\"U_StartDt\", " + upTo + ") + 1) / 365.0 END AS DECIMAL(19, 2)) AS \"Prorata\", " +
                   invoiced + " AS \"Facture\", " +
                   "(SELECT COUNT(*) FROM " + Db.T(Db.ContractEq) + " e WHERE e.\"Code\" = c.\"Code\" AND ISNULL(e.\"U_Equip\", '') <> '') AS \"NbEq\", " +
                   "CASE WHEN ISNULL(c.\"U_Active\", 'Y') = 'N' THEN N'Inactif' " +
                   " WHEN c.\"U_EndDt\" < CAST(GETDATE() AS DATE) THEN N'Échu' " +
                   " WHEN DATEADD(day, -ISNULL(c.\"U_Notice\", 0), c.\"U_EndDt\") < CAST(GETDATE() AS DATE) THEN N'Préavis dépassé (reconduction)' " +
                   " WHEN DATEADD(day, -ISNULL(c.\"U_Notice\", 0) - 30, c.\"U_EndDt\") < CAST(GETDATE() AS DATE) THEN N'À décider (préavis < 30 j)' " +
                   " WHEN c.\"U_StartDt\" > CAST(GETDATE() AS DATE) THEN N'À venir' ELSE N'Actif' END AS \"Etat\" " +
                   "FROM " + Db.T(Db.Contract) + " c LEFT JOIN \"OCRD\" b ON b.\"CardCode\" = c.\"U_Vendor\" " +
                   "WHERE 1 = 1" +
                   (string.IsNullOrEmpty(f.Equip) ? "" : " AND EXISTS (SELECT 1 FROM " + Db.T(Db.ContractEq) + " e WHERE e.\"Code\" = c.\"Code\" AND e.\"U_Equip\" = " + Sql.Q(f.Equip) + ")") +
                   " ORDER BY c.\"U_EndDt\"";
        }

        private static string ShipmentsSql(ReportFilter f)
        {
            return "SELECT NULLIF(s.\"U_OrderNo\", 0) AS \"Key\", s.\"U_SentDate\" AS \"Envoi\", s.\"U_Equip\" AS \"Equip\", e.\"Name\" AS \"EqName\", " +
                   "ISNULL(b.\"CardName\", s.\"U_Vendor\") AS \"Prest\", o.\"DocNum\" AS \"Num\", s.\"U_ExpRet\" AS \"RetPrev\", s.\"U_RetDate\" AS \"RetReel\", " +
                   ShipStatus.List.SqlCase("s.\"U_Status\"") + " AS \"Statut\", " +
                   "DATEDIFF(day, s.\"U_SentDate\", ISNULL(s.\"U_RetDate\", GETDATE())) AS \"Jours\", " +
                   "CASE WHEN s.\"U_Status\" = 'O' AND s.\"U_ExpRet\" < CAST(GETDATE() AS DATE) THEN N'Oui' ELSE N'' END AS \"Retard\", " +
                   "s.\"U_Reason\" AS \"Motif\", s.\"U_RetNote\" AS \"Comment\" " +
                   "FROM " + Db.T(Db.Ship) + " s LEFT JOIN " + Db.T(Db.Equip) + " e ON e.\"Code\" = s.\"U_Equip\" " +
                   "LEFT JOIN \"OCRD\" b ON b.\"CardCode\" = s.\"U_Vendor\" LEFT JOIN " + Db.T(Db.Order) + " o ON o.\"DocEntry\" = s.\"U_OrderNo\" " +
                   // Les envois en cours restent visibles quelle que soit la période
                   "WHERE (s.\"U_Status\" = 'O' OR s.\"U_SentDate\" BETWEEN " + Sql.D(f.From) + " AND " + Sql.D(f.To) + ")" +
                   Eq("s.\"U_Status\"", f.Status) + Eq("s.\"U_Equip\"", f.Equip) +
                   " ORDER BY s.\"U_Status\", s.\"U_SentDate\" DESC";
        }

        private static string WarrantiesSql(ReportFilter f)
        {
            return "SELECT e.\"Code\" AS \"Equip\", e.\"Name\" AS \"EqName\", e.\"U_FuncLoc\" AS \"Poste\", " +
                   "ISNULL(b.\"CardName\", COALESCE(NULLIF(e.\"U_WarrVend\", ''), e.\"U_Vendor\")) AS \"Garant\", e.\"U_WarrEnd\" AS \"FinGar\", " +
                   "DATEDIFF(day, GETDATE(), e.\"U_WarrEnd\") AS \"JRest\", " +
                   "CASE WHEN e.\"U_WarrEnd\" < CAST(GETDATE() AS DATE) THEN N'Expirée' WHEN e.\"U_WarrEnd\" < DATEADD(day, 60, GETDATE()) THEN N'Expire sous 60 j' ELSE N'Sous garantie' END AS \"Etat\", " +
                   "(SELECT COUNT(*) FROM " + Db.T(Db.Order) + " o WHERE o.\"U_Equip\" = e.\"Code\" AND o.\"U_UnderWar\" = 'Y') AS \"NbOrd\" " +
                   "FROM " + Db.T(Db.Equip) + " e LEFT JOIN \"OCRD\" b ON b.\"CardCode\" = COALESCE(NULLIF(e.\"U_WarrVend\", ''), e.\"U_Vendor\") " +
                   "WHERE e.\"U_WarrEnd\" IS NOT NULL" + Between("e.\"U_WarrEnd\"", f) + Eq("e.\"Code\"", f.Equip) +
                   " ORDER BY e.\"U_WarrEnd\"";
        }

        /// <summary>Ordres et factures d'un contrat (onglet de la fiche contrat).</summary>
        public static string ContractActivitySql(string contract)
        {
            return "SELECT N'" + Obj.Order + "' AS \"KeyObj\", o.\"DocEntry\" AS \"Key\", o.\"U_StartDt\" AS \"Date\", N'Ordre' AS \"Mvt\", o.\"DocNum\" AS \"Num\", " +
                   "o.\"U_Equip\" AS \"Equip\", o.\"U_Subject\" AS \"Objet\", " + OrderStatus.List.SqlCase("o.\"U_Status\"") + " AS \"Statut\", " + Actual + " AS \"Montant\" " +
                   "FROM " + Db.T(Db.Order) + " o WHERE o.\"U_Contract\" = " + Sql.Q(contract) +
                   " UNION ALL SELECT N'', 0, h.\"DocDate\", N'Facture fournisseur', h.\"DocNum\", N'', l.\"Dscription\", N'', l.\"LineTotal\" " +
                   "FROM \"PCH1\" l JOIN \"OPCH\" h ON h.\"DocEntry\" = l.\"DocEntry\" WHERE h.\"CANCELED\" = 'N' AND l.\"" + DocFields.Contract + "\" = " + Sql.Q(contract) +
                   " UNION ALL SELECT N'', 0, h.\"DocDate\", N'Avoir fournisseur', h.\"DocNum\", N'', l.\"Dscription\", N'', -l.\"LineTotal\" " +
                   "FROM \"RPC1\" l JOIN \"ORPC\" h ON h.\"DocEntry\" = l.\"DocEntry\" WHERE h.\"CANCELED\" = 'N' AND l.\"" + DocFields.Contract + "\" = " + Sql.Q(contract) +
                   " ORDER BY \"Date\" DESC";
        }

        private static string KpiSql(ReportFilter f)
        {
            double hours = Math.Max(1, (f.To.Date.AddDays(1) - f.From.Date).TotalHours);
            string h = hours.ToString("0", CultureInfo.InvariantCulture);
            return "SELECT e.\"Code\" AS \"Equip\", e.\"Name\" AS \"EqName\", ISNULL(k.\"Name\", e.\"U_Category\") AS \"Categ\", e.\"U_Critic\" AS \"Crit\", " +
                   "ISNULL(o.\"Nb\", 0) AS \"NbOrd\", ISNULL(n.\"Nb\", 0) AS \"NbPan\", CAST(ISNULL(n.\"Arret\", 0) AS DECIMAL(19, 2)) AS \"ArretH\", " +
                   "CAST(CASE WHEN ISNULL(n.\"Nb\", 0) = 0 THEN 0 ELSE n.\"Arret\" / n.\"Nb\" END AS DECIMAL(19, 2)) AS \"MTTR\", " +
                   "CAST(CASE WHEN ISNULL(n.\"Nb\", 0) = 0 THEN " + h + " ELSE (" + h + " - n.\"Arret\") / n.\"Nb\" END AS DECIMAL(19, 2)) AS \"MTBF\", " +
                   "CAST(100.0 * (" + h + " - ISNULL(n.\"Arret\", 0)) / " + h + " AS DECIMAL(9, 2)) AS \"Dispo\", " +
                   "ISNULL(o.\"Lab\", 0) AS \"CoutMO\", ISNULL(o.\"Mat\", 0) AS \"CoutMat\", ISNULL(o.\"Ext\", 0) AS \"CoutExt\", " +
                   "ISNULL(o.\"Lab\", 0) + ISNULL(o.\"Mat\", 0) + ISNULL(o.\"Ext\", 0) AS \"CoutTot\" " +
                   "FROM " + Db.T(Db.Equip) + " e LEFT JOIN " + Db.T(Db.EqCat) + " k ON k.\"Code\" = e.\"U_Category\" " +
                   "LEFT JOIN (SELECT o.\"U_Equip\", COUNT(*) AS \"Nb\", SUM(o.\"U_AcLab\") AS \"Lab\", SUM(o.\"U_AcMat\") AS \"Mat\", SUM(o.\"U_AcExt\") AS \"Ext\" " +
                   "  FROM " + Db.T(Db.Order) + " o WHERE o.\"U_Status\" <> 'CANC'" + Between("o.\"U_StartDt\"", f) + " GROUP BY o.\"U_Equip\") o ON o.\"U_Equip\" = e.\"Code\" " +
                   "LEFT JOIN (SELECT n.\"U_Equip\", COUNT(*) AS \"Nb\", SUM(" + Downtime("n") + ") AS \"Arret\" " +
                   "  FROM " + Db.T(Db.Notif) + " n WHERE n.\"U_Breakdwn\" = 'Y'" + Between("n.\"U_MalfStD\"", f) + " GROUP BY n.\"U_Equip\") n ON n.\"U_Equip\" = e.\"Code\" " +
                   "WHERE (o.\"Nb\" > 0 OR n.\"Nb\" > 0)" + Eq("e.\"Code\"", f.Equip) +
                   " ORDER BY \"CoutTot\" DESC, \"NbPan\" DESC";
        }

        private static string CostsSql(ReportFilter f)
        {
            return "SELECT x.\"Mois\", " + OrderTypes.List.SqlCase("x.\"T\"") + " AS \"Type\", x.\"NbOrd\", x.\"Prevu\", x.\"CoutMO\", x.\"CoutMat\", x.\"CoutExt\", " +
                   "x.\"CoutMO\" + x.\"CoutMat\" + x.\"CoutExt\" AS \"CoutTot\", x.\"CoutMO\" + x.\"CoutMat\" + x.\"CoutExt\" - x.\"Prevu\" AS \"Ecart\" FROM (" +
                   "SELECT CONVERT(CHAR(7), o.\"U_StartDt\", 126) AS \"Mois\", o.\"U_OrdType\" AS \"T\", COUNT(*) AS \"NbOrd\", SUM(" + Planned + ") AS \"Prevu\", " +
                   "SUM(o.\"U_AcLab\") AS \"CoutMO\", SUM(o.\"U_AcMat\") AS \"CoutMat\", SUM(o.\"U_AcExt\") AS \"CoutExt\" " +
                   "FROM " + Db.T(Db.Order) + " o WHERE o.\"U_Status\" <> 'CANC'" + Between("o.\"U_StartDt\"", f) +
                   Eq("o.\"U_OrdType\"", f.Type) + Eq("o.\"U_Equip\"", f.Equip) + Eq("o.\"U_WorkCtr\"", f.WorkCtr) +
                   " GROUP BY CONVERT(CHAR(7), o.\"U_StartDt\", 126), o.\"U_OrdType\") x ORDER BY x.\"Mois\", x.\"T\"";
        }

        private static string ConfirmationsSql(ReportFilter f)
        {
            return "SELECT c.\"U_OrderNo\" AS \"Key\", c.\"U_ConfDate\" AS \"Date\", o.\"DocNum\" AS \"Num\", c.\"U_OpNo\" AS \"Op\", o.\"U_Equip\" AS \"Equip\", " +
                   "c.\"U_EmpName\" AS \"Salarie\", c.\"U_WorkCtr\" AS \"Poste\", c.\"U_Hours\" AS \"Heures\", c.\"U_Amount\" AS \"Montant\", " +
                   "CASE WHEN c.\"U_Final\" = 'Y' THEN N'Oui' ELSE N'' END AS \"Finale\", NULLIF(c.\"U_TransId\", 0) AS \"Ecriture\", " +
                   "c.\"U_Remarks\" AS \"Comment\", c.\"U_User\" AS \"Util\" " +
                   "FROM " + Db.T(Db.Conf) + " c JOIN " + Db.T(Db.Order) + " o ON o.\"DocEntry\" = c.\"U_OrderNo\" " +
                   "WHERE 1 = 1" + Between("c.\"U_ConfDate\"", f) + Eq("o.\"U_Equip\"", f.Equip) + Eq("c.\"U_WorkCtr\"", f.WorkCtr) +
                   " ORDER BY c.\"U_ConfDate\" DESC, c.\"Code\" DESC";
        }

        private static string MaterialsSql(ReportFilter f)
        {
            string common = " JOIN " + Db.T(Db.Order) + " o ON o.\"DocEntry\" = l.\"" + DocFields.Order + "\" WHERE h.\"CANCELED\" = 'N'" +
                            Between("h.\"DocDate\"", f) + Eq("o.\"U_Equip\"", f.Equip);
            return "SELECT o.\"DocEntry\" AS \"Key\", h.\"DocDate\" AS \"Date\", N'Sortie' AS \"Mvt\", h.\"DocNum\" AS \"Doc\", o.\"DocNum\" AS \"Num\", " +
                   "o.\"U_Equip\" AS \"Equip\", l.\"ItemCode\" AS \"Article\", l.\"Dscription\" AS \"Desig\", l.\"Quantity\" AS \"Qte\", " +
                   "l.\"StockSum\" AS \"Valeur\", l.\"WhsCode\" AS \"Mag\" FROM \"IGE1\" l JOIN \"OIGE\" h ON h.\"DocEntry\" = l.\"DocEntry\"" + common +
                   " UNION ALL SELECT o.\"DocEntry\", h.\"DocDate\", N'Retour', h.\"DocNum\", o.\"DocNum\", o.\"U_Equip\", l.\"ItemCode\", l.\"Dscription\", " +
                   "-l.\"Quantity\", -l.\"LineTotal\", l.\"WhsCode\" FROM \"IGN1\" l JOIN \"OIGN\" h ON h.\"DocEntry\" = l.\"DocEntry\"" + common +
                   " ORDER BY \"Date\" DESC, \"Doc\" DESC";
        }

        private static string MeasurementsSql(ReportFilter f)
        {
            return "SELECT m.\"U_MDate\" AS \"Date\", RIGHT('0' + CAST(ISNULL(m.\"U_MTime\", 0) / 100 AS VARCHAR(2)), 2) + ':' + " +
                   "RIGHT('0' + CAST(ISNULL(m.\"U_MTime\", 0) % 100 AS VARCHAR(2)), 2) AS \"Heure\", m.\"U_Equip\" AS \"Equip\", e.\"Name\" AS \"EqName\", " +
                   "m.\"U_Point\" AS \"Point\", p.\"U_Descr\" AS \"Desig\", m.\"U_Value\" AS \"Valeur\", p.\"U_Unit\" AS \"Unite\", m.\"U_Diff\" AS \"Diff\", " +
                   "m.\"U_Remarks\" AS \"Comment\", NULLIF(m.\"U_NotifNo\", 0) AS \"Avis\", m.\"U_User\" AS \"Util\" " +
                   "FROM " + Db.T(Db.MeasDoc) + " m LEFT JOIN " + Db.T(Db.Equip) + " e ON e.\"Code\" = m.\"U_Equip\" " +
                   "LEFT JOIN " + Db.T(Db.EquipPts) + " p ON p.\"Code\" = m.\"U_Equip\" AND p.\"U_Point\" = m.\"U_Point\" " +
                   "WHERE 1 = 1" + Between("m.\"U_MDate\"", f) + Eq("m.\"U_Equip\"", f.Equip) +
                   " ORDER BY m.\"U_MDate\" DESC, m.\"U_MTime\" DESC, m.\"Code\" DESC";
        }

        private static string CallsSql(ReportFilter f)
        {
            return "SELECT NULLIF(c.\"U_OrderNo\", 0) AS \"Key\", c.\"U_CallDate\" AS \"Date\", c.\"U_PlanCode\" AS \"Plan\", p.\"Name\" AS \"Desig\", " +
                   "p.\"U_Equip\" AS \"Equip\", c.\"U_DueDate\" AS \"Echeance\", NULLIF(c.\"U_DueCnt\", 0) AS \"EchCpt\", " +
                   CallStatus.List.SqlCase("c.\"U_Status\"") + " AS \"Statut\", o.\"DocNum\" AS \"Num\", " +
                   OrderStatus.List.SqlCase("o.\"U_Status\"") + " AS \"Etat\", c.\"U_DoneDate\" AS \"Realise\", c.\"U_User\" AS \"Util\" " +
                   "FROM " + Db.T(Db.Call) + " c LEFT JOIN " + Db.T(Db.Plan) + " p ON p.\"Code\" = c.\"U_PlanCode\" " +
                   "LEFT JOIN " + Db.T(Db.Order) + " o ON o.\"DocEntry\" = c.\"U_OrderNo\" " +
                   "WHERE 1 = 1" + Between("c.\"U_CallDate\"", f) + Eq("c.\"U_Status\"", f.Status) + Eq("p.\"U_Equip\"", f.Equip) +
                   " ORDER BY c.\"U_CallDate\" DESC, c.\"Code\" DESC";
        }

        // ---------------------------------------------------------------------
        // Historique d'un équipement (onglet de la fiche équipement)
        // ---------------------------------------------------------------------

        public static string EquipmentHistorySql(string equip)
        {
            return "SELECT N'" + Obj.Order + "' AS \"KeyObj\", o.\"DocEntry\" AS \"Key\", o.\"U_StartDt\" AS \"Date\", N'Ordre' AS \"Nature\", o.\"DocNum\" AS \"Num\", " +
                   OrderTypes.List.SqlCase("o.\"U_OrdType\"") + " AS \"Type\", " + OrderStatus.List.SqlCase("o.\"U_Status\"") + " AS \"Statut\", " +
                   "o.\"U_Subject\" AS \"Objet\", " + Actual + " AS \"Reel\", CAST(0 AS DECIMAL(19, 2)) AS \"Panne\" " +
                   "FROM " + Db.T(Db.Order) + " o WHERE o.\"U_Equip\" = " + Sql.Q(equip) +
                   " UNION ALL SELECT N'" + Obj.Notif + "', n.\"DocEntry\", n.\"U_RepDate\", N'Avis', n.\"DocNum\", " +
                   NotifTypes.List.SqlCase("n.\"U_Type\"") + ", " + NotifStatus.List.SqlCase("n.\"U_Status\"") + ", n.\"U_Subject\", 0, " +
                   "CAST(" + Downtime("n") + " AS DECIMAL(19, 2)) " +
                   "FROM " + Db.T(Db.Notif) + " n WHERE n.\"U_Equip\" = " + Sql.Q(equip) +
                   " UNION ALL SELECT N'" + Obj.Order + "', ISNULL(s.\"U_OrderNo\", 0), s.\"U_SentDate\", N'Envoi prestataire', ISNULL(o.\"DocNum\", 0), " +
                   "ISNULL(b.\"CardName\", s.\"U_Vendor\"), " + ShipStatus.List.SqlCase("s.\"U_Status\"") + ", s.\"U_Reason\", 0, 0 " +
                   "FROM " + Db.T(Db.Ship) + " s LEFT JOIN \"OCRD\" b ON b.\"CardCode\" = s.\"U_Vendor\" LEFT JOIN " + Db.T(Db.Order) + " o ON o.\"DocEntry\" = s.\"U_OrderNo\" " +
                   "WHERE s.\"U_Equip\" = " + Sql.Q(equip) +
                   " ORDER BY \"Date\" DESC, \"Num\" DESC";
        }

        /// <summary>Postes techniques et équipements rattachés directement à un poste technique.</summary>
        public static string FuncLocContentSql(string funcLoc)
        {
            return "SELECT N'" + Obj.FuncLoc + "' AS \"KeyObj\", \"Code\" AS \"Key\", N'Poste technique' AS \"Nature\", \"Code\" AS \"Element\", \"Name\" AS \"Desig\", N'' AS \"Etat\" " +
                   "FROM " + Db.T(Db.FuncLoc) + " WHERE \"U_Parent\" = " + Sql.Q(funcLoc) +
                   " UNION ALL SELECT N'" + Obj.Equip + "', \"Code\", N'Équipement', \"Code\", \"Name\", " + EquipStatus.List.SqlCase("\"U_Status\"") +
                   " FROM " + Db.T(Db.Equip) + " WHERE \"U_FuncLoc\" = " + Sql.Q(funcLoc) + " AND ISNULL(\"U_Parent\", '') = ''" +
                   " ORDER BY \"Nature\" DESC, \"Element\"";
        }

        public static string PlanCallsSql(string plan)
        {
            return "SELECT NULLIF(c.\"U_OrderNo\", 0) AS \"Key\", c.\"U_CallDate\" AS \"Date\", c.\"U_DueDate\" AS \"Echeance\", NULLIF(c.\"U_DueCnt\", 0) AS \"EchCpt\", " +
                   CallStatus.List.SqlCase("c.\"U_Status\"") + " AS \"Statut\", o.\"DocNum\" AS \"Num\", " +
                   OrderStatus.List.SqlCase("o.\"U_Status\"") + " AS \"Etat\", c.\"U_DoneDate\" AS \"Realise\" " +
                   "FROM " + Db.T(Db.Call) + " c LEFT JOIN " + Db.T(Db.Order) + " o ON o.\"DocEntry\" = c.\"U_OrderNo\" " +
                   "WHERE c.\"U_PlanCode\" = " + Sql.Q(plan) + " ORDER BY c.\"Code\" DESC";
        }

        public static string OrderConfirmationsSql(int order)
        {
            return "SELECT c.\"U_ConfDate\" AS \"Date\", c.\"U_OpNo\" AS \"Op\", c.\"U_EmpName\" AS \"Salarie\", c.\"U_WorkCtr\" AS \"Poste\", " +
                   "c.\"U_Hours\" AS \"Heures\", c.\"U_Amount\" AS \"Montant\", CASE WHEN c.\"U_Final\" = 'Y' THEN N'Oui' ELSE N'' END AS \"Finale\", " +
                   "NULLIF(c.\"U_TransId\", 0) AS \"Ecriture\", c.\"U_Remarks\" AS \"Comment\" " +
                   "FROM " + Db.T(Db.Conf) + " c WHERE c.\"U_OrderNo\" = " + order + " ORDER BY c.\"Code\"";
        }

        /// <summary>Documents SAP rattachés à l'ordre (sorties, retours, achats).</summary>
        public static string OrderDocumentsSql(int order)
        {
            string w = " WHERE h.\"CANCELED\" = 'N' AND l.\"" + DocFields.Order + "\" = " + order;
            return "SELECT N'Sortie de stock' AS \"Mvt\", h.\"DocNum\" AS \"Doc\", h.\"DocDate\" AS \"Date\", l.\"ItemCode\" AS \"Article\", l.\"Dscription\" AS \"Desig\", l.\"Quantity\" AS \"Qte\", l.\"StockSum\" AS \"Valeur\" FROM \"IGE1\" l JOIN \"OIGE\" h ON h.\"DocEntry\" = l.\"DocEntry\"" + w +
                   " UNION ALL SELECT N'Retour en stock', h.\"DocNum\", h.\"DocDate\", l.\"ItemCode\", l.\"Dscription\", -l.\"Quantity\", -l.\"LineTotal\" FROM \"IGN1\" l JOIN \"OIGN\" h ON h.\"DocEntry\" = l.\"DocEntry\"" + w +
                   " UNION ALL SELECT N'Demande d''achat', h.\"DocNum\", h.\"DocDate\", l.\"ItemCode\", l.\"Dscription\", l.\"Quantity\", l.\"LineTotal\" FROM \"PRQ1\" l JOIN \"OPRQ\" h ON h.\"DocEntry\" = l.\"DocEntry\"" + w +
                   " UNION ALL SELECT N'Commande d''achat', h.\"DocNum\", h.\"DocDate\", l.\"ItemCode\", l.\"Dscription\", l.\"Quantity\", l.\"LineTotal\" FROM \"POR1\" l JOIN \"OPOR\" h ON h.\"DocEntry\" = l.\"DocEntry\"" + w +
                   " UNION ALL SELECT N'Facture fournisseur', h.\"DocNum\", h.\"DocDate\", l.\"ItemCode\", l.\"Dscription\", l.\"Quantity\", l.\"LineTotal\" FROM \"PCH1\" l JOIN \"OPCH\" h ON h.\"DocEntry\" = l.\"DocEntry\"" + w +
                   " UNION ALL SELECT N'Avoir fournisseur', h.\"DocNum\", h.\"DocDate\", l.\"ItemCode\", l.\"Dscription\", -l.\"Quantity\", -l.\"LineTotal\" FROM \"RPC1\" l JOIN \"ORPC\" h ON h.\"DocEntry\" = l.\"DocEntry\"" + w +
                   " ORDER BY \"Date\", \"Mvt\"";
        }
    }
}
