using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using SAPbobsCOM;
using MaintenanceAddon.Core;
using MaintenanceAddon.Models;

namespace MaintenanceAddon.Services
{
    internal sealed class ContractInfo
    {
        public string Code = "";
        public string Name = "";
        public string Vendor = "";
        public string Type = "";
        public DateTime? Start;
        public DateTime? End;
        public int ResponseHours;
        public bool CoversLabor;
        public bool CoversParts;

        /// <summary>
        /// Le contrat s'applique-t-il à ce type d'ordre ? Préventif (P) : PM02/PM04 ;
        /// dépannage (D) : PM01 ; complet (C) : PM01/PM02/PM04. Les améliorations (PM03) sont hors contrat.
        /// </summary>
        public bool Covers(string orderType)
        {
            bool preventive = orderType == OrderTypes.Preventive || orderType == OrderTypes.Inspection;
            bool corrective = orderType == OrderTypes.Corrective;
            switch (Type)
            {
                case "P": return preventive;
                case "D": return corrective;
                default: return preventive || corrective;
            }
        }
    }

    /// <summary>
    /// Contexte d'intervention sur un équipement à une date : garantie,
    /// contrat de maintenance, prestataire à solliciter par défaut.
    /// </summary>
    internal sealed class ServiceContext
    {
        public EquipmentInfo Equipment;
        public bool UnderWarranty;
        public ContractInfo Contract;

        /// <summary>Prestataire par défaut : garant, sinon titulaire du contrat, sinon prestataire attitré.</summary>
        public string DefaultVendor
        {
            get
            {
                if (UnderWarranty && Equipment.WarrantyVendor != "")
                    return Equipment.WarrantyVendor;
                if (Contract != null && Contract.Vendor != "")
                    return Contract.Vendor;
                return Equipment?.MaintVendor ?? "";
            }
        }

        /// <summary>La main-d'oeuvre externe est déjà payée (garantie ou contrat qui la couvre).</summary>
        public bool LaborCovered => UnderWarranty || (Contract != null && Contract.CoversLabor);

        /// <param name="orderType">Type d'ordre envisagé (null : tout contrat actif est retenu).</param>
        public static ServiceContext For(string equip, DateTime date, string orderType = null)
        {
            EquipmentInfo eq = EquipmentInfo.Load(equip);
            if (eq == null)
                return null;
            ContractInfo c = ContractService.ActiveFor(equip, date);
            if (c != null && orderType != null && !c.Covers(orderType))
                c = null;
            return new ServiceContext
            {
                Equipment = eq,
                // La garantie couvre les pannes, pas l'entretien courant ni les améliorations
                UnderWarranty = eq.UnderWarranty(date) && (orderType == null || orderType == OrderTypes.Corrective),
                Contract = c
            };
        }

        /// <summary>Bandeau d'information pour les écrans (vide s'il n'y a rien à signaler).</summary>
        public string Banner()
        {
            var parts = new List<string>();
            if (UnderWarranty)
                parts.Add("SOUS GARANTIE jusqu'au " + Equipment.WarrantyEnd.Value.ToString("dd/MM/yyyy") +
                          (Equipment.WarrantyVendor != "" ? " (garant " + VendorName(Equipment.WarrantyVendor) + ")" : ""));
            if (Contract != null)
                parts.Add("Contrat " + Contract.Code + " - " + VendorName(Contract.Vendor) +
                          (Contract.ResponseHours > 0 ? ", intervention sous " + Contract.ResponseHours + " h" : "") +
                          (Contract.CoversLabor ? ", main-d'oeuvre incluse" : "") + (Contract.CoversParts ? ", pièces incluses" : ""));
            if (Equipment.Status == EquipStatus.AtVendor)
                parts.Add("Équipement actuellement chez le prestataire");
            return string.Join("  |  ", parts);
        }

        public static string VendorName(string cardCode)
        {
            if (string.IsNullOrEmpty(cardCode))
                return "";
            string name = Sql.ScalarStr("SELECT \"CardName\" FROM \"OCRD\" WHERE \"CardCode\" = " + Sql.Q(cardCode));
            return name == "" ? cardCode : name;
        }
    }

    /// <summary>Contrats de maintenance avec les prestataires.</summary>
    internal static class ContractService
    {
        /// <summary>Contrat actif couvrant l'équipement à la date (le plus récent), ou null.</summary>
        public static ContractInfo ActiveFor(string equip, DateTime date)
        {
            if (string.IsNullOrEmpty(equip))
                return null;
            Row r = Sql.First(
                "SELECT TOP 1 c.* FROM " + Db.T(Db.Contract) + " c JOIN " + Db.T(Db.ContractEq) + " l ON l.\"Code\" = c.\"Code\" " +
                "WHERE l.\"U_Equip\" = " + Sql.Q(equip) + " AND ISNULL(c.\"U_Active\", 'Y') = 'Y' " +
                "AND (c.\"U_StartDt\" IS NULL OR c.\"U_StartDt\" <= " + Sql.D(date) + ") " +
                "AND (c.\"U_EndDt\" IS NULL OR c.\"U_EndDt\" >= " + Sql.D(date) + ") ORDER BY c.\"U_EndDt\" DESC");
            return r == null ? null : ToInfo(r);
        }

        public static ContractInfo Load(string code)
        {
            Row r = Sql.First("SELECT * FROM " + Db.T(Db.Contract) + " WHERE \"Code\" = " + Sql.Q(code));
            return r == null ? null : ToInfo(r);
        }

        private static ContractInfo ToInfo(Row r)
        {
            return new ContractInfo
            {
                Code = r.Str("Code"),
                Name = r.Str("Name"),
                Vendor = r.Str("U_Vendor"),
                Type = r.Str("U_Type"),
                Start = r.Date("U_StartDt"),
                End = r.Date("U_EndDt"),
                ResponseHours = r.Int("U_RespHrs"),
                CoversLabor = r.Str("U_CovLab") == "Y",
                CoversParts = r.Str("U_CovParts") == "Y"
            };
        }

        /// <summary>Montant facturé sur le contrat (factures − avoirs fournisseurs portant le code contrat).</summary>
        public static double Invoiced(string code, DateTime? from = null, DateTime? to = null)
        {
            string period = (from.HasValue ? " AND h.\"DocDate\" >= " + Sql.D(from.Value) : "") + (to.HasValue ? " AND h.\"DocDate\" <= " + Sql.D(to.Value) : "");
            return Sql.ScalarDbl(
                "SELECT ISNULL((SELECT SUM(l.\"LineTotal\") FROM \"PCH1\" l JOIN \"OPCH\" h ON h.\"DocEntry\" = l.\"DocEntry\" WHERE h.\"CANCELED\" = 'N' AND l.\"" + DocFields.Contract + "\" = " + Sql.Q(code) + period + "), 0) - " +
                "ISNULL((SELECT SUM(l.\"LineTotal\") FROM \"RPC1\" l JOIN \"ORPC\" h ON h.\"DocEntry\" = l.\"DocEntry\" WHERE h.\"CANCELED\" = 'N' AND l.\"" + DocFields.Contract + "\" = " + Sql.Q(code) + period + "), 0)");
        }

        /// <summary>Montant prévu au contrat sur la période couverte jusqu'à la date (prorata journalier).</summary>
        public static double ExpectedToDate(ContractInfo c, double annualAmount, DateTime date)
        {
            if (c.Start == null || annualAmount <= 0)
                return 0;
            DateTime end = c.End.HasValue && c.End.Value < date ? c.End.Value : date;
            double days = (end.Date - c.Start.Value.Date).TotalDays + 1;
            return days <= 0 ? 0 : Math.Round(annualAmount * days / 365.0, 2);
        }
    }

    /// <summary>Envoi d'un équipement en réparation chez un prestataire, et son retour.</summary>
    internal static class ShipmentService
    {
        /// <summary>Envoi en cours de l'équipement (code de la ligne), ou null.</summary>
        public static Row OpenShipment(string equip)
        {
            return Sql.First("SELECT TOP 1 * FROM " + Db.T(Db.Ship) + " WHERE \"U_Equip\" = " + Sql.Q(equip) +
                             " AND \"U_Status\" = " + Sql.Q(ShipStatus.Out) + " ORDER BY \"Code\" DESC");
        }

        /// <summary>Enregistre l'envoi : l'équipement passe « Chez le prestataire ».</summary>
        public static string Send(string equip, string vendor, int orderEntry, DateTime sent, DateTime? expectedReturn, string reason)
        {
            EquipmentInfo eq = EquipmentInfo.Load(equip);
            if (eq == null)
                throw new InvalidOperationException("Équipement inconnu : " + equip);
            if (eq.Status == EquipStatus.Scrapped)
                throw new InvalidOperationException("L'équipement est mis au rebut.");
            Row open = OpenShipment(equip);
            if (open != null)
                throw new InvalidOperationException("L'équipement est déjà chez " + ServiceContext.VendorName(open.Str("U_Vendor")) +
                                                    " depuis le " + open.Date("U_SentDate")?.ToString("dd/MM/yyyy") + ".");
            if (string.IsNullOrEmpty(vendor) || !Sql.Exists("SELECT 1 FROM \"OCRD\" WHERE \"CardType\" = 'S' AND \"CardCode\" = " + Sql.Q(vendor)))
                throw new InvalidOperationException("Indiquez un fournisseur (prestataire) existant.");
            if (sent > DateTime.Today)
                throw new InvalidOperationException("La date d'envoi ne peut pas être dans le futur.");
            if (expectedReturn.HasValue && expectedReturn.Value < sent)
                throw new InvalidOperationException("Le retour prévu précède la date d'envoi.");
            if (orderEntry > 0 && Sql.ScalarStr("SELECT \"U_Equip\" FROM " + Db.T(Db.Order) + " WHERE \"DocEntry\" = " + orderEntry) != equip)
                throw new InvalidOperationException("L'ordre indiqué ne porte pas sur cet équipement.");

            return DiCompany.InTransaction(() =>
            {
                string code = Sql.NextCode(Db.Ship);
                UserTable t = DiCompany.Instance.UserTables.Item(Db.Ship);
                try
                {
                    t.Code = code;
                    t.Name = code;
                    Fields f = t.UserFields.Fields;
                    f.Item("U_Equip").Value = equip;
                    f.Item("U_Vendor").Value = vendor;
                    f.Item("U_OrderNo").Value = orderEntry;
                    f.Item("U_SentDate").Value = sent;
                    if (expectedReturn.HasValue)
                        f.Item("U_ExpRet").Value = expectedReturn.Value;
                    f.Item("U_Status").Value = ShipStatus.Out;
                    f.Item("U_PrevSt").Value = eq.Status == "" ? EquipStatus.Active : eq.Status;
                    f.Item("U_Reason").Value = NotificationService.Truncate(reason, 200);
                    f.Item("U_User").Value = DiCompany.UserCode;
                    DiCompany.ThrowIfError(t.Add(), "Enregistrement de l'envoi");
                }
                finally
                {
                    Marshal.ReleaseComObject(t);
                }
                SetEquipmentStatus(equip, EquipStatus.AtVendor);
                return code;
            });
        }

        /// <summary>Retour de l'équipement : il reprend son statut d'avant l'envoi.</summary>
        public static void Return(string equip, DateTime returned, string note)
        {
            Row open = OpenShipment(equip);
            if (open == null)
                throw new InvalidOperationException("Aucun envoi en cours pour l'équipement " + equip + ".");
            DateTime sent = open.Date("U_SentDate") ?? DateTime.MinValue;
            if (returned < sent)
                throw new InvalidOperationException("La date de retour précède la date d'envoi (" + sent.ToString("dd/MM/yyyy") + ").");
            if (returned > DateTime.Today)
                throw new InvalidOperationException("La date de retour ne peut pas être dans le futur.");

            DiCompany.InTransaction(() =>
            {
                UserTable t = DiCompany.Instance.UserTables.Item(Db.Ship);
                try
                {
                    if (!t.GetByKey(open.Str("Code")))
                        throw new InvalidOperationException("Envoi introuvable.");
                    t.UserFields.Fields.Item("U_RetDate").Value = returned;
                    t.UserFields.Fields.Item("U_Status").Value = ShipStatus.Returned;
                    t.UserFields.Fields.Item("U_RetNote").Value = NotificationService.Truncate(note, 200);
                    DiCompany.ThrowIfError(t.Update(), "Enregistrement du retour");
                }
                finally
                {
                    Marshal.ReleaseComObject(t);
                }
                string prev = open.Str("U_PrevSt");
                SetEquipmentStatus(equip, prev == "" || prev == EquipStatus.AtVendor ? EquipStatus.Active : prev);
            });
        }

        private static void SetEquipmentStatus(string equip, string status)
        {
            UdoData e = UdoData.Get(Obj.Equip, equip);
            e.Set("U_Status", status);
            e.Update();
        }

        public static string Describe(Row open)
        {
            if (open == null)
                return "";
            DateTime? exp = open.Date("U_ExpRet");
            string late = exp.HasValue && exp.Value < DateTime.Today ? " - RETOUR EN RETARD" : "";
            return "Chez " + ServiceContext.VendorName(open.Str("U_Vendor")) + " depuis le " + open.Date("U_SentDate")?.ToString("dd/MM/yyyy") +
                   (exp.HasValue ? ", retour prévu le " + exp.Value.ToString("dd/MM/yyyy") : "") + late;
        }
    }
}
