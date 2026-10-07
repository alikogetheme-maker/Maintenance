using System;
using System.Collections.Generic;
using System.Globalization;
using MaintenanceAddon.Core;
using MaintenanceAddon.Models;

namespace MaintenanceAddon.Services
{
    /// <summary>N° de série reçu dans SAP (OSRN) et document d'entrée qui l'a reçu (OITL / ITL1).</summary>
    internal sealed class SerialInfo
    {
        public string ItemCode = "";
        public string ItemName = "";
        public int SysNumber;
        public string Serial = "";
        public string MnfSerial = "";
        public string Manufacturer = "";
        public DateTime? InDate;
        public DateTime? MnfDate;
        public DateTime? WarrantyEnd;
        public string Vendor = "";
        public string VendorName = "";
        public string DocLabel = "";
        public string DocNum = "";
        public DateTime? DocDate;
        public double UnitCost;
        /// <summary>Équipement déjà rattaché à ce n° de série (vide sinon).</summary>
        public string Equipment = "";

        /// <summary>« Réception de marchandises n° 12 du 03/10/2026 (fournisseur) ».</summary>
        public string Source()
        {
            if (DocLabel == "")
                return "";
            return DocLabel + " n° " + DocNum + (DocDate.HasValue ? " du " + DocDate.Value.ToString("dd/MM/yyyy") : "") +
                   (VendorName != "" ? " - " + VendorName : "");
        }
    }

    /// <summary>
    /// Lien des équipements avec les données SAP : article et n° de série reçu,
    /// immobilisation (si le module est utilisé), pièces de rechange, doublons.
    /// </summary>
    internal static class EquipmentService
    {
        /// <summary>Documents d'entrée en stock d'un n° de série (OITL.DocType).</summary>
        private static string DocLabelSql(string col)
        {
            return "CASE " + col + " WHEN 20 THEN N'Réception de marchandises' WHEN 18 THEN N'Facture fournisseur' " +
                   "WHEN 59 THEN N'Entrée de marchandises' WHEN 67 THEN N'Transfert' WHEN 16 THEN N'Retour client' ELSE N'Document ' + CAST(" + col + " AS NVARCHAR(10)) END";
        }

        private static string DocLabel(int docType)
        {
            switch (docType)
            {
                case 0: return "";
                case 20: return "Réception de marchandises";
                case 18: return "Facture fournisseur";
                case 59: return "Entrée de marchandises";
                case 67: return "Transfert";
                case 16: return "Retour client";
                default: return "Document " + docType.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Prix unitaire de la ligne d'entrée en devise société, remises déduites, hors
        /// taxes : montant de la ligne / quantité en unité de stock (StockPrice n'est pas toujours renseigné).
        /// </summary>
        private static string LineCostSql(string t)
        {
            string unit = "COALESCE(NULLIF(x.\"StockPrice\", 0), x.\"LineTotal\" / NULLIF(COALESCE(NULLIF(x.\"InvQty\", 0), x.\"Quantity\"), 0))";
            string where = " x WHERE x.\"DocEntry\" = " + t + ".\"DocEntry\" AND x.\"LineNum\" = " + t + ".\"DocLine\")";
            return "CASE " + t + ".\"DocType\" " +
                   "WHEN 20 THEN (SELECT " + unit + " FROM \"PDN1\"" + where + " " +
                   "WHEN 18 THEN (SELECT " + unit + " FROM \"PCH1\"" + where + " " +
                   "WHEN 59 THEN (SELECT " + unit + " FROM \"IGN1\"" + where + " END";
        }

        /// <summary>Première entrée en stock du n° de série.</summary>
        private const string FirstReceipt =
            "OUTER APPLY (SELECT TOP 1 h.\"DocType\", h.\"DocEntry\", h.\"DocLine\", h.\"DocNum\", h.\"DocDate\", h.\"CardCode\", h.\"CardName\" " +
            " FROM \"OITL\" h JOIN \"ITL1\" l ON l.\"LogEntry\" = h.\"LogEntry\" " +
            " WHERE l.\"ItemCode\" = s.\"ItemCode\" AND l.\"SysNumber\" = s.\"SysNumber\" AND l.\"Quantity\" > 0 ORDER BY h.\"LogEntry\") t ";

        /// <summary>Équipement rattaché au n° de série (même article, même n°).</summary>
        private static string LinkedEquipSql()
        {
            return "(SELECT TOP 1 e.\"Code\" FROM " + Db.T(Db.Equip) + " e WHERE e.\"U_ItemCode\" = s.\"ItemCode\" AND e.\"U_SerialNo\" = s.\"DistNumber\")";
        }

        /// <summary>N° de série reçu pour l'article, ou null.</summary>
        public static SerialInfo Serial(string itemCode, string serial)
        {
            if (string.IsNullOrEmpty(itemCode) || string.IsNullOrEmpty(serial))
                return null;
            Row r = Sql.First(
                "SELECT s.\"ItemCode\", i.\"ItemName\", s.\"SysNumber\", s.\"DistNumber\", s.\"MnfSerial\", s.\"InDate\", s.\"MnfDate\", s.\"GrntExp\", " +
                " ISNULL(m.\"FirmName\", N'') AS \"Firm\", t.\"DocType\", t.\"DocNum\", t.\"DocDate\", t.\"CardCode\", t.\"CardName\", " +
                " COALESCE(NULLIF(s.\"CostTotal\", 0), " + LineCostSql("t") + ", 0) AS \"Cost\", " + LinkedEquipSql() + " AS \"Equip\" " +
                "FROM \"OSRN\" s JOIN \"OITM\" i ON i.\"ItemCode\" = s.\"ItemCode\" LEFT JOIN \"OMRC\" m ON m.\"FirmCode\" = i.\"FirmCode\" " + FirstReceipt +
                "WHERE s.\"ItemCode\" = " + Sql.Q(itemCode) + " AND s.\"DistNumber\" = " + Sql.Q(serial));
            if (r == null)
                return null;
            int docType = r.Int("DocType");
            bool purchase = docType == 20 || docType == 18;
            return new SerialInfo
            {
                ItemCode = r.Str("ItemCode"),
                ItemName = r.Str("ItemName"),
                SysNumber = r.Int("SysNumber"),
                Serial = r.Str("DistNumber"),
                MnfSerial = r.Str("MnfSerial"),
                Manufacturer = r.Str("Firm"),
                InDate = r.Date("InDate"),
                MnfDate = r.Date("MnfDate"),
                WarrantyEnd = r.Date("GrntExp"),
                Vendor = purchase ? r.Str("CardCode") : "",
                VendorName = purchase ? r.Str("CardName") : "",
                DocLabel = DocLabel(docType),
                DocNum = r.Str("DocNum"),
                DocDate = r.Date("DocDate"),
                UnitCost = Math.Round(r.Dbl("Cost"), 2),
                Equipment = r.Str("Equip")
            };
        }

        /// <summary>N° de série reçus (liste de choix) : article, texte cherché, seulement les n° libres.</summary>
        public static string ReceivedSerialsSql(string itemCode, string text, bool onlyFree)
        {
            string like = string.IsNullOrWhiteSpace(text) ? "" :
                " AND (s.\"DistNumber\" LIKE " + Sql.Q("%" + text.Trim() + "%") + " OR s.\"MnfSerial\" LIKE " + Sql.Q("%" + text.Trim() + "%") +
                " OR i.\"ItemName\" LIKE " + Sql.Q("%" + text.Trim() + "%") + ")";
            return "SELECT s.\"ItemCode\" AS \"Article\", i.\"ItemName\" AS \"Desig\", s.\"DistNumber\" AS \"Serie\", s.\"MnfSerial\" AS \"SerFab\", " +
                   "s.\"InDate\" AS \"Date\", " + DocLabelSql("t.\"DocType\"") + " + N' ' + CAST(t.\"DocNum\" AS NVARCHAR(20)) AS \"DocSrc\", " +
                   "CASE WHEN t.\"DocType\" IN (18, 20) THEN t.\"CardName\" ELSE N'' END AS \"Fourn\", " +
                   "CAST(COALESCE(NULLIF(s.\"CostTotal\", 0), " + LineCostSql("t") + ", 0) AS DECIMAL(19, 2)) AS \"Valeur\", " +
                   "s.\"GrntExp\" AS \"FinGar\", " + LinkedEquipSql() + " AS \"Equip\" " +
                   "FROM \"OSRN\" s JOIN \"OITM\" i ON i.\"ItemCode\" = s.\"ItemCode\" " + FirstReceipt +
                   "WHERE 1 = 1" + (string.IsNullOrEmpty(itemCode) ? "" : " AND s.\"ItemCode\" = " + Sql.Q(itemCode)) + like +
                   (onlyFree ? " AND " + LinkedEquipSql() + " IS NULL" : "") +
                   " ORDER BY s.\"InDate\" DESC, s.\"ItemCode\", s.\"DistNumber\"";
        }

        /// <summary>Le module Immobilisations est utilisé : au moins un article de type immobilisation.</summary>
        public static bool FixedAssetsUsed()
        {
            return Sql.Exists("SELECT TOP 1 1 FROM \"OITM\" WHERE \"ItemType\" = 'F'");
        }

        /// <summary>
        /// Autre équipement portant le même n° de série (même article, ou même
        /// fabricant si aucun article n'est indiqué), ou la même immobilisation.
        /// </summary>
        public static string Duplicate(string code, string itemCode, string serial, string manufacturer, string asset)
        {
            string other = "\"Code\" <> " + Sql.Q(code ?? "");
            if (!string.IsNullOrEmpty(serial))
            {
                string dup = Sql.ScalarStr("SELECT TOP 1 \"Code\" FROM " + Db.T(Db.Equip) + " WHERE " + other + " AND \"U_SerialNo\" = " + Sql.Q(serial) +
                    (string.IsNullOrEmpty(itemCode)
                        ? " AND ISNULL(\"U_ItemCode\", '') = '' AND ISNULL(\"U_Manuf\", '') = " + Sql.Q(manufacturer ?? "")
                        : " AND \"U_ItemCode\" = " + Sql.Q(itemCode)));
                if (dup != "")
                    return "Le n° de série " + serial + " est déjà celui de l'équipement " + dup + " (" + NotificationService.NameOf(Db.Equip, dup) + ").";
            }
            if (!string.IsNullOrEmpty(asset))
            {
                string dup = Sql.ScalarStr("SELECT TOP 1 \"Code\" FROM " + Db.T(Db.Equip) + " WHERE " + other + " AND \"U_AssetNo\" = " + Sql.Q(asset));
                if (dup != "")
                    return "L'immobilisation " + asset + " est déjà rattachée à l'équipement " + dup + ".";
            }
            return null;
        }

        /// <summary>Contrôle des liens SAP de la fiche ; renvoie un message d'erreur ou null.</summary>
        public static string CheckLinks(string code, string itemCode, string serial, string manufacturer, string asset)
        {
            if (!string.IsNullOrEmpty(itemCode))
            {
                string type = Sql.ScalarStr("SELECT \"ItemType\" FROM \"OITM\" WHERE \"ItemCode\" = " + Sql.Q(itemCode));
                if (type == "")
                    return "Article inconnu : " + itemCode;
                if (type == "F")
                    return "L'article " + itemCode + " est une immobilisation : indiquez-le dans le champ « Immobilisation ».";
            }
            if (!string.IsNullOrEmpty(asset) && Sql.ScalarStr("SELECT \"ItemType\" FROM \"OITM\" WHERE \"ItemCode\" = " + Sql.Q(asset)) != "F")
                return "L'immobilisation " + asset + " n'existe pas dans SAP (article de type immobilisation).";
            return Duplicate(code, itemCode, serial, manufacturer, asset);
        }

        /// <summary>Prochain code d'équipement (préfixe des paramètres + 5 chiffres), vide sans préfixe.</summary>
        public static string NextCode()
        {
            string prefix = SettingsService.Load().EquipPrefix;
            if (string.IsNullOrEmpty(prefix))
                return "";
            double max = Sql.ScalarDbl(
                "SELECT MAX(CAST(SUBSTRING(\"Code\", " + (prefix.Length + 1) + ", 20) AS BIGINT)) FROM " + Db.T(Db.Equip) +
                " WHERE \"Code\" LIKE " + Sql.Q(prefix + "%") + " AND ISNUMERIC(SUBSTRING(\"Code\", " + (prefix.Length + 1) + ", 20)) = 1" +
                " AND SUBSTRING(\"Code\", " + (prefix.Length + 1) + ", 20) NOT LIKE '%[^0-9]%'");
            return prefix + ((long)max + 1).ToString("D5", CultureInfo.InvariantCulture);
        }

        /// <summary>Valeurs de la fiche reprises d'un n° de série reçu (alias de champ → valeur).</summary>
        public static Dictionary<string, object> FieldsFrom(SerialInfo s)
        {
            var f = new Dictionary<string, object>
            {
                { "U_ItemCode", s.ItemCode },
                { "U_SerialNo", s.Serial },
                { "U_SerSys", s.SysNumber }
            };
            if (s.Manufacturer != "")
                f["U_Manuf"] = s.Manufacturer;
            if (s.Vendor != "")
                f["U_Vendor"] = s.Vendor;
            DateTime? acq = s.DocDate ?? s.InDate;
            if (acq.HasValue)
                f["U_AcqDate"] = acq.Value;
            if (s.UnitCost > 0)
                f["U_AcqValue"] = s.UnitCost;
            if (s.WarrantyEnd.HasValue)
                f["U_WarrEnd"] = s.WarrantyEnd.Value;
            if (s.MnfDate.HasValue)
                f["U_ConstYear"] = s.MnfDate.Value.Year;
            return f;
        }

        /// <summary>Crée l'équipement d'un n° de série reçu ; renvoie son code.</summary>
        public static string CreateFromSerial(string itemCode, string serial, string code, string name, string funcLoc)
        {
            AuthService.Require(Perm.MasterData, "créer un équipement");
            SerialInfo s = Serial(itemCode, serial);
            if (s == null)
                throw new InvalidOperationException("Le n° de série " + serial + " n'a pas été reçu dans SAP pour l'article " + itemCode + ".");
            if (s.Equipment != "")
                throw new InvalidOperationException("Le n° de série " + serial + " est déjà rattaché à l'équipement " + s.Equipment + ".");
            if (string.IsNullOrEmpty(code))
                code = NextCode();
            if (string.IsNullOrEmpty(code))
                throw new InvalidOperationException("Indiquez le code de l'équipement (aucun préfixe de numérotation dans les paramètres).");

            UdoData e = UdoData.New(Obj.Equip);
            e.Set("Code", code);
            e.Set("Name", NotificationService.Truncate(string.IsNullOrEmpty(name) ? s.ItemName : name, 100));
            e.Set("U_Status", EquipStatus.Active);
            e.Set("U_StartUp", DateTime.Today);
            if (!string.IsNullOrEmpty(funcLoc))
            {
                e.Set("U_FuncLoc", funcLoc);
                Row fl = Sql.First("SELECT \"U_OcrCode\", \"U_Whs\" FROM " + Db.T(Db.FuncLoc) + " WHERE \"Code\" = " + Sql.Q(funcLoc));
                if (fl != null)
                {
                    e.Set("U_OcrCode", fl.Str("U_OcrCode"));
                    e.Set("U_Whs", fl.Str("U_Whs"));
                }
            }
            foreach (var f in FieldsFrom(s))
                e.Set(f.Key, f.Value);
            e.Add();
            return code;
        }

        /// <summary>Pièces de rechange de l'équipement avec le stock disponible du magasin.</summary>
        public static List<Row> SpareParts(string equip, string whs)
        {
            return Sql.Rows(
                "SELECT p.\"LineId\", p.\"U_ItemCode\", ISNULL(NULLIF(p.\"U_ItemName\", ''), i.\"ItemName\") AS \"U_ItemName\", p.\"U_Qty\", " +
                " ISNULL(i.\"InvntItem\", 'N') AS \"InvntItem\", " +
                " ISNULL((SELECT w.\"OnHand\" - w.\"IsCommited\" FROM \"OITW\" w WHERE w.\"ItemCode\" = p.\"U_ItemCode\" AND w.\"WhsCode\" = " + Sql.Q(whs ?? "") + "), 0) AS \"Dispo\" " +
                "FROM " + Db.T(Db.EquipParts) + " p LEFT JOIN \"OITM\" i ON i.\"ItemCode\" = p.\"U_ItemCode\" " +
                "WHERE p.\"Code\" = " + Sql.Q(equip) + " AND ISNULL(p.\"U_ItemCode\", '') <> '' ORDER BY p.\"LineId\"");
        }
    }
}
