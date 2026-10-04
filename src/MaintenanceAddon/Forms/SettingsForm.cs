using System;
using System.Globalization;
using SAPbouiCOM;
using MaintenanceAddon.Core;
using MaintenanceAddon.Services;

namespace MaintenanceAddon.Forms
{
    /// <summary>Paramètres de l'add-on : comptes, analytique, délais par priorité, ordonnancement.</summary>
    internal sealed class SettingsForm : SimpleForm
    {
        public SettingsForm(Application app) : base(app) { }

        protected override string FormType => FormIds.SetupForm;
        protected override string Title => "Paramètres de la maintenance";
        protected override int FormWidth => 600;
        protected override int FormHeight => 490;

        public void Show()
        {
            if (Open())
                Load();
        }

        protected override void Build()
        {
            const int lw = 250, x = 10;
            int y = 10;
            foreach (string id in new[] { "udExp", "udLabE", "udLabA", "udCap" })
                AddUds(id, BoDataType.dt_SHORT_TEXT, 15);
            AddUds("udPost", BoDataType.dt_SHORT_TEXT, 1);
            AddUds("udDim", BoDataType.dt_SHORT_TEXT, 1);
            AddUds("udWhs", BoDataType.dt_SHORT_TEXT, 8);
            AddUds("udPref", BoDataType.dt_SHORT_TEXT, 5);
            foreach (string id in new[] { "udP1", "udP2", "udP3", "udP4", "udHor" })
                AddUds(id, BoDataType.dt_LONG_NUMBER);

            U.Label("lSec1", "Comptabilisation", x, y, 300);
            y += Ui.Step + 2;
            AccountField("lExp", "Compte de charges de maintenance (pièces, prestations)", "eExp", "udExp", "cflExp", x, y, lw);
            y += Ui.Step;
            U.CheckUds("cPost", "Comptabiliser la main-d'oeuvre à chaque confirmation", x, y, 400, "udPost");
            y += Ui.Step;
            AccountField("lLabE", "Compte de charges de main-d'oeuvre", "eLabE", "udLabE", "cflLabE", x, y, lw);
            y += Ui.Step;
            AccountField("lLabA", "Compte d'imputation de la main-d'oeuvre (crédit)", "eLabA", "udLabA", "cflLabA", x, y, lw);
            y += Ui.Step;
            AccountField("lCap", "Compte d'immobilisations en cours (ordres à immobiliser)", "eCap", "udCap", "cflCap", x, y, lw);
            y += Ui.Step;
            U.Label("lDim", "Axe analytique des centres de coûts", x, y, lw, "cDim");
            ComboBox dim = U.ComboUds("cDim", x + lw, y, 150, "udDim");
            foreach (Row r in Sql.Rows("SELECT \"DimCode\", \"DimDesc\" FROM \"ODIM\" ORDER BY \"DimCode\""))
                dim.ValidValues.Add(r.Int("DimCode").ToString(CultureInfo.InvariantCulture), r.Int("DimCode") + " - " + r.Str("DimDesc"));
            y += Ui.Step + 10;

            U.Label("lSec2", "Données de base", x, y, 300);
            y += Ui.Step + 2;
            U.Cfl("cflWhs", "64");
            U.Label("lWhs", "Magasin de pièces par défaut", x, y, lw, "eWhs");
            Ui.BindCfl(U.EditUds("eWhs", x + lw, y, 100, "udWhs"), "cflWhs", "WhsCode");
            y += Ui.Step;
            U.Label("lPref", "Préfixe de numérotation des équipements", x, y, lw, "ePref");
            U.EditUds("ePref", x + lw, y, 60, "udPref");
            y += Ui.Step + 10;

            U.Label("lSec3", "Délai de réalisation par priorité (heures)", x, y, 300);
            y += Ui.Step + 2;
            string[] labels = { "1 - Très élevée", "2 - Élevée", "3 - Moyenne", "4 - Faible" };
            for (int i = 0; i < 4; i++)
            {
                U.Label("lP" + (i + 1), labels[i], x, y, lw, "eP" + (i + 1));
                U.EditUds("eP" + (i + 1), x + lw, y, 60, "udP" + (i + 1));
                y += Ui.Step;
            }
            y += 6;
            U.Label("lHor", "Horizon d'ordonnancement par défaut (jours)", x, y, lw, "eHor");
            U.EditUds("eHor", x + lw, y, 60, "udHor");

            U.Button("bSave", "Enregistrer", x, FormHeight - 62, 100);
            U.Button("bClose", "Fermer", x + 105, FormHeight - 62, 90);
        }

        private void AccountField(string labelId, string caption, string id, string uds, string cflId, int x, int y, int lw)
        {
            ChooseFromList cfl = U.Cfl(cflId, "1");
            Ui.CflFilter(cfl, "Postable", "Y");
            U.Label(labelId, caption, x, y, lw, id);
            Ui.BindCfl(U.EditUds(id, x + lw, y, 120, uds), cflId, "AcctCode");
        }

        private void Load()
        {
            Settings s = SettingsService.Load();
            SetUds("udExp", s.ExpenseAccount);
            SetUds("udLabE", s.LaborExpenseAccount);
            SetUds("udLabA", s.LaborAbsorptionAccount);
            SetUds("udCap", s.CapitalAccount);
            SetUds("udPost", s.PostLabor ? "Y" : "N");
            SetUds("udDim", s.Dimension.ToString(CultureInfo.InvariantCulture));
            SetUds("udWhs", s.DefaultWarehouse);
            SetUds("udPref", s.EquipPrefix);
            for (int i = 0; i < 4; i++)
                SetUds("udP" + (i + 1), s.PriorityHours[i].ToString(CultureInfo.InvariantCulture));
            SetUds("udHor", s.HorizonDays.ToString(CultureInfo.InvariantCulture));
        }

        protected override void OnEvent(ItemEvent e)
        {
            if (e.EventType == BoEventTypes.et_CHOOSE_FROM_LIST)
            {
                string uds = e.ItemUID == "eExp" ? "udExp" : e.ItemUID == "eLabE" ? "udLabE" : e.ItemUID == "eLabA" ? "udLabA" :
                             e.ItemUID == "eCap" ? "udCap" : e.ItemUID == "eWhs" ? "udWhs" : null;
                string value = uds == null ? null : Chosen(e, uds == "udWhs" ? "WhsCode" : "AcctCode");
                if (value != null)
                    SetUds(uds, value);
                return;
            }
            if (e.EventType != BoEventTypes.et_ITEM_PRESSED || !e.ActionSuccess)
                return;
            if (e.ItemUID == "bClose")
                Close();
            else if (e.ItemUID == "bSave")
                Save();
        }

        private void Save()
        {
            var s = new Settings
            {
                ExpenseAccount = Uds("udExp").Trim(),
                LaborExpenseAccount = Uds("udLabE").Trim(),
                LaborAbsorptionAccount = Uds("udLabA").Trim(),
                CapitalAccount = Uds("udCap").Trim(),
                PostLabor = Uds("udPost") == "Y",
                Dimension = int.TryParse(Uds("udDim"), out int d) && d >= 1 && d <= 5 ? d : 1,
                DefaultWarehouse = Uds("udWhs").Trim(),
                EquipPrefix = Uds("udPref").Trim(),
                HorizonDays = Math.Max(1, (int)Sql.ParseDouble(Uds("udHor")))
            };
            for (int i = 0; i < 4; i++)
            {
                int h = (int)Sql.ParseDouble(Uds("udP" + (i + 1)));
                if (h <= 0)
                {
                    Program.Message(App, "Le délai de la priorité " + (i + 1) + " doit être positif.");
                    return;
                }
                s.PriorityHours[i] = h;
            }
            SettingsService.Save(s);
            Msg("Paramètres enregistrés.");
        }
    }
}
