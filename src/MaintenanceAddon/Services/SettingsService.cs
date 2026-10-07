using System;
using System.Runtime.InteropServices;
using SAPbobsCOM;
using MaintenanceAddon.Core;

namespace MaintenanceAddon.Services
{
    internal sealed class Settings
    {
        public string ExpenseAccount = "";
        public string LaborExpenseAccount = "";
        public string LaborAbsorptionAccount = "";
        public bool PostLabor;
        public int Dimension = 1;
        public string DefaultWarehouse = "";
        public string EquipPrefix = "EQ";
        public int[] PriorityHours = { 4, 24, 72, 168 };
        public int HorizonDays = 30;
        /// <summary>Compte de règlement par défaut des ordres à immobiliser (immobilisations en cours).</summary>
        public string CapitalAccount = "";
        /// <summary>Utilisateurs SAP qui reçoivent les alertes (codes séparés par des virgules).</summary>
        public string AlertUsers = "";
        /// <summary>Horizon des alertes : garanties et contrats qui expirent dans ce nombre de jours.</summary>
        public int AlertDays = 30;
        /// <summary>Date du dernier envoi des alertes quotidiennes.</summary>
        public DateTime? AlertDate;

        /// <summary>Délai de réalisation (heures) d'une priorité 1 à 4.</summary>
        public int HoursFor(string priority)
        {
            int p;
            if (!int.TryParse(priority, out p) || p < 1 || p > 4)
                p = 3;
            return PriorityHours[p - 1];
        }
    }

    /// <summary>Paramétrage de l'add-on (table @MNT_SETUP, ligne "1").</summary>
    internal static class SettingsService
    {
        public static Settings Load()
        {
            Row r = Sql.First("SELECT * FROM " + Db.T(Db.Setup) + " WHERE \"Code\" = " + Sql.Q(Db.SetupCode));
            var s = new Settings();
            if (r == null)
                return s;
            s.ExpenseAccount = r.Str("U_ExpAcct");
            s.LaborExpenseAccount = r.Str("U_LabExpAcct");
            s.LaborAbsorptionAccount = r.Str("U_LabAbsAcct");
            s.PostLabor = r.Str("U_PostLab") == "Y";
            s.Dimension = Math.Max(1, Math.Min(5, r.Int("U_DimCode") == 0 ? 1 : r.Int("U_DimCode")));
            s.DefaultWarehouse = r.Str("U_DfltWhs");
            s.EquipPrefix = r.Str("U_EqPrefix");
            s.PriorityHours = new[]
            {
                Positive(r.Int("U_P1Hours"), 4), Positive(r.Int("U_P2Hours"), 24),
                Positive(r.Int("U_P3Hours"), 72), Positive(r.Int("U_P4Hours"), 168)
            };
            s.HorizonDays = Positive(r.Int("U_Horizon"), 30);
            s.CapitalAccount = r.Str("U_CapAcct");
            s.AlertUsers = r.Str("U_AlertUsr");
            s.AlertDays = Positive(r.Int("U_AlertDays"), 30);
            s.AlertDate = r.Date("U_AlertDt");
            return s;
        }

        private static int Positive(int value, int fallback)
        {
            return value > 0 ? value : fallback;
        }

        public static void Save(Settings s)
        {
            AuthService.Require(Perm.Setup, "modifier les paramètres");
            CheckAccount(s.ExpenseAccount, "Compte de charges de maintenance");
            CheckAccount(s.LaborExpenseAccount, "Compte de charges de main-d'oeuvre");
            CheckAccount(s.LaborAbsorptionAccount, "Compte d'imputation de main-d'oeuvre");
            CheckAccount(s.CapitalAccount, "Compte d'immobilisations en cours");
            if (s.PostLabor && (s.LaborExpenseAccount == "" || s.LaborAbsorptionAccount == ""))
                throw new InvalidOperationException("Pour comptabiliser la main-d'oeuvre, renseignez les deux comptes de main-d'oeuvre.");
            if (s.DefaultWarehouse != "" && !Sql.Exists("SELECT 1 FROM \"OWHS\" WHERE \"WhsCode\" = " + Sql.Q(s.DefaultWarehouse)))
                throw new InvalidOperationException("Magasin inconnu : " + s.DefaultWarehouse);
            s.AlertUsers = AlertService.NormalizeUsers(s.AlertUsers);

            UserTable table = DiCompany.Instance.UserTables.Item(Db.Setup);
            try
            {
                if (!table.GetByKey(Db.SetupCode))
                    throw new InvalidOperationException("Ligne de paramétrage introuvable (redémarrez l'add-on).");
                Fields f = table.UserFields.Fields;
                f.Item("U_ExpAcct").Value = s.ExpenseAccount;
                f.Item("U_LabExpAcct").Value = s.LaborExpenseAccount;
                f.Item("U_LabAbsAcct").Value = s.LaborAbsorptionAccount;
                f.Item("U_PostLab").Value = s.PostLabor ? "Y" : "N";
                f.Item("U_DimCode").Value = s.Dimension;
                f.Item("U_DfltWhs").Value = s.DefaultWarehouse;
                f.Item("U_EqPrefix").Value = s.EquipPrefix;
                f.Item("U_P1Hours").Value = s.PriorityHours[0];
                f.Item("U_P2Hours").Value = s.PriorityHours[1];
                f.Item("U_P3Hours").Value = s.PriorityHours[2];
                f.Item("U_P4Hours").Value = s.PriorityHours[3];
                f.Item("U_Horizon").Value = s.HorizonDays;
                f.Item("U_CapAcct").Value = s.CapitalAccount;
                f.Item("U_AlertUsr").Value = s.AlertUsers;
                f.Item("U_AlertDays").Value = s.AlertDays;
                DiCompany.ThrowIfError(table.Update(), "Enregistrement des paramètres");
            }
            finally
            {
                Marshal.ReleaseComObject(table);
            }
        }

        /// <summary>Compte de bilan/gestion existant, non titre, autorisé en saisie manuelle.</summary>
        public static void CheckAccount(string account, string label)
        {
            if (string.IsNullOrEmpty(account))
                return;
            Row r = Sql.First("SELECT \"Postable\", \"FrozenFor\", \"LocManTran\" FROM \"OACT\" WHERE \"AcctCode\" = " + Sql.Q(account));
            if (r == null)
                throw new InvalidOperationException(label + " : compte " + account + " inconnu.");
            if (r.Str("Postable") != "Y")
                throw new InvalidOperationException(label + " : le compte " + account + " est un compte titre.");
            if (r.Str("FrozenFor") == "Y")
                throw new InvalidOperationException(label + " : le compte " + account + " est bloqué.");
            // Les écritures de l'add-on (main-d'oeuvre, règlement) sont des écritures manuelles
            if (r.Str("LocManTran") == "Y")
                throw new InvalidOperationException(label + " : le compte " + account + " est un compte collectif (écritures manuelles interdites).");
        }

        /// <summary>Renseigne le centre de coûts sur l'axe paramétré (1 à 5).</summary>
        public static void SetCostingCode(Document_Lines line, int dimension, string ocrCode)
        {
            if (string.IsNullOrEmpty(ocrCode))
                return;
            switch (dimension)
            {
                case 2: line.CostingCode2 = ocrCode; break;
                case 3: line.CostingCode3 = ocrCode; break;
                case 4: line.CostingCode4 = ocrCode; break;
                case 5: line.CostingCode5 = ocrCode; break;
                default: line.CostingCode = ocrCode; break;
            }
        }

        public static void SetCostingCode(JournalEntries_Lines line, int dimension, string ocrCode)
        {
            if (string.IsNullOrEmpty(ocrCode))
                return;
            switch (dimension)
            {
                case 2: line.CostingCode2 = ocrCode; break;
                case 3: line.CostingCode3 = ocrCode; break;
                case 4: line.CostingCode4 = ocrCode; break;
                case 5: line.CostingCode5 = ocrCode; break;
                default: line.CostingCode = ocrCode; break;
            }
        }
    }
}
