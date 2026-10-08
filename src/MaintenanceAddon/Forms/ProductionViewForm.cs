using System;
using System.Collections.Generic;
using System.Globalization;
using SAPbouiCOM;
using MaintenanceAddon.Core;
using MaintenanceAddon.Models;
using MaintenanceAddon.Services;

namespace MaintenanceAddon.Forms
{
    /// <summary>
    /// Suivi de la maintenance d'un ordre de fabrication : synthèse (arrêts, pertes,
    /// coût), points à vérifier sur la ligne, interventions, pièces consommées,
    /// pièces de rechange des machines de la ligne.
    /// </summary>
    internal sealed class ProductionViewForm : SimpleForm
    {
        private const string DtInt = "dtPInt", DtPcs = "dtPPcs", DtCrit = "dtPCrit";
        private int _of;

        public ProductionViewForm(Application app) : base(app) { }

        protected override string FormType => FormIds.ProductionForm;
        protected override string Title => "Maintenance de la production";
        protected override int FormWidth => 1000;
        protected override int FormHeight => 700;

        public void Show(int ofEntry)
        {
            string refusal = AuthService.Denied(Perm.Order, Access.Read, "consulter le suivi de maintenance de la production");
            if (refusal != null)
            {
                Program.Message(App, refusal);
                return;
            }
            Open();
            if (ofEntry > 0)
                SetUds("udOf", ofEntry.ToString(CultureInfo.InvariantCulture));
            LoadOrder();
        }

        protected override void Build()
        {
            // Texte : une liste de choix ne peut être liée qu'à une zone alphanumérique
            AddUds("udOf", BoDataType.dt_SHORT_TEXT, 11);
            AddUds("udHead", BoDataType.dt_LONG_TEXT, 254);
            AddUds("udSum", BoDataType.dt_LONG_TEXT, 254);
            AddUds("udWarn", BoDataType.dt_LONG_TEXT, 4000);

            U.Cfl("cflOf", "202");
            U.Label("lOf", "Ordre de fabrication (n° interne)", 10, 10, 190, "eOf");
            Ui.BindCfl(U.EditUds("eOf", 200, 10, 90, "udOf"), "cflOf", "DocEntry");
            U.Button("bShow", "Afficher", 300, 8, 90);
            U.ReadOnlyUds("eHead", 10, 32, FormWidth - 40, "udHead");
            U.ReadOnlyUds("eSum", 10, 49, FormWidth - 40, "udSum");

            U.Label("lWarn", "Points à vérifier sur la ligne (machines, pannes, préventifs, pièces)", 10, 72, 600);
            Item w = F.Items.Add("eWarn", BoFormItemTypes.it_EXTEDIT);
            w.Left = 10; w.Top = 89; w.Width = FormWidth - 40; w.Height = 60;
            w.AffectsFormMode = false;
            ((EditText)w.Specific).DataBind.SetBound(true, "", "udWarn");
            w.Enabled = false;

            U.Label("lInt", "Interventions (double-clic pour ouvrir l'avis ou l'ordre)", 10, 156, 600);
            U.Grid("gInt", DtInt, 10, 173, FormWidth - 40, 200);
            U.Label("lPcs", "Pièces consommées par ces interventions", 10, 381, 380);
            U.Grid("gPcs", DtPcs, 10, 398, 380, 220);
            U.Label("lCrit", "Pièces de rechange des machines de la ligne", 400, 381, 400);
            U.Grid("gCrit", DtCrit, 400, 398, FormWidth - 430, 220);

            U.Button("bNotif", "Déclarer une panne", 10, FormHeight - 62, 140);
            U.Button("bOpenOf", "Ouvrir l'OF", 155, FormHeight - 62, 100);
            U.Button("bRefresh", "Actualiser", 260, FormHeight - 62, 100);
            U.Button("bClose", "Fermer", 365, FormHeight - 62, 90);
        }

        private void LoadOrder()
        {
            int.TryParse(Uds("udOf").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _of);
            ProdOrderInfo of = ProductionService.Load(_of);
            F.Freeze(true);
            try
            {
                if (of == null)
                {
                    SetUds("udHead", _of > 0 ? "Ordre de fabrication introuvable." : "Choisissez un ordre de fabrication (Tab dans le champ), puis Afficher.");
                    SetUds("udSum", "");
                    SetUds("udWarn", "");
                    U.LoadGrid("gInt", DtInt, "SELECT TOP 0 N'' AS \"Nature\"");
                    U.LoadGrid("gPcs", DtPcs, "SELECT TOP 0 N'' AS \"Article\"");
                    U.LoadGrid("gCrit", DtCrit, "SELECT TOP 0 N'' AS \"Article\"");
                    return;
                }
                F.Title = "Maintenance de la production - OF n° " + of.DocNum;
                var fr = CultureInfo.GetCultureInfo("fr-FR");
                SetUds("udHead", of.Label() + "  |  " + ProdStatus.List.Caption(of.Status) + ", du " + of.From.ToString("dd/MM/yyyy") +
                                 (of.Closed.HasValue ? " au " + of.Closed.Value.ToString("dd/MM/yyyy") : " (en cours)") +
                                 "  |  produit " + of.CompletedQty.ToString("N0", fr) + " / " + of.PlannedQty.ToString("N0", fr) +
                                 (of.RejectedQty > 0 ? ", rebuts " + of.RejectedQty.ToString("N0", fr) : ""));
                SetUds("udSum", of.Line == ""
                    ? "Aucune ligne de production : renseignez le champ « Ligne de production (maint.) » de l'OF ou la ligne par défaut de l'article."
                    : ProductionService.Summary(of));
                List<string> warnings = ProductionService.ReleaseWarnings(of.Line);
                SetUds("udWarn", warnings.Count == 0 ? (of.Line == "" ? "" : "Rien à signaler sur les machines de la ligne.") : "- " + string.Join("\r\n- ", warnings));
                U.LoadGrid("gInt", DtInt, ProductionService.InterventionsSql(of));
                U.LoadGrid("gPcs", DtPcs, ProductionService.PartsSql(of));
                U.LoadGrid("gCrit", DtCrit, ProductionService.CriticalPartsSql(of.Line));
            }
            finally
            {
                F.Freeze(false);
            }
        }

        protected override void OnEvent(ItemEvent e)
        {
            switch (e.EventType)
            {
                case BoEventTypes.et_CHOOSE_FROM_LIST:
                    {
                        string entry = Chosen(e, "DocEntry");
                        if (entry != null)
                        {
                            SetUds("udOf", entry);
                            LoadOrder();
                        }
                        return;
                    }
                case BoEventTypes.et_DOUBLE_CLICK:
                    if (e.ItemUID == "gInt" && e.Row >= 0)
                        UdoForm.OpenGridRow(F, "gInt", DtInt, null, e.Row);
                    return;
                case BoEventTypes.et_ITEM_PRESSED:
                    if (!e.ActionSuccess)
                        return;
                    switch (e.ItemUID)
                    {
                        case "bShow":
                        case "bRefresh":
                            LoadOrder();
                            break;
                        case "bClose":
                            Close();
                            break;
                        case "bOpenOf":
                            if (_of > 0)
                                App.OpenForm(BoFormObjectEnum.fo_ProductionOrder, "", _of.ToString(CultureInfo.InvariantCulture));
                            break;
                        case "bNotif":
                            if (ProductionService.Load(_of) == null)
                            {
                                Msg("Choisissez d'abord un ordre de fabrication.", BoStatusBarMessageType.smt_Warning);
                                return;
                            }
                            Navigator.Notification.NewForProduction(_of);
                            break;
                    }
                    return;
            }
        }
    }

    /// <summary>
    /// Écran standard « Ordre de fabrication » de SAP : bouton Maintenance (suivi de l'OF)
    /// et vérification des machines de la ligne au lancement de l'OF.
    /// </summary>
    internal static class ProductionOrderHook
    {
        public const string SapFormType = "65211";
        private const string Button = "MNTbPrd";
        private static Application _app;

        public static void Register(Application app)
        {
            _app = app;
            EventHub.RegisterFormType(app, SapFormType, OnItem);
        }

        private static void OnItem(string formUID, ref ItemEvent e, out bool bubbleEvent)
        {
            bubbleEvent = true;
            try
            {
                if (e.EventType == BoEventTypes.et_FORM_LOAD && !e.BeforeAction)
                    AddButton(_app.Forms.Item(formUID));
                else if (e.EventType == BoEventTypes.et_ITEM_PRESSED && !e.BeforeAction && e.ItemUID == Button)
                    OpenView(_app.Forms.Item(formUID));
                else if (e.EventType == BoEventTypes.et_ITEM_PRESSED && e.BeforeAction && e.ItemUID == "1")
                    bubbleEvent = CheckSave(_app.Forms.Item(formUID));
            }
            catch (Exception ex)
            {
                // Jamais bloquer l'écran standard de SAP à cause de l'add-on
                Program.Log("Écran OF : " + ex);
            }
        }

        /// <summary>Bouton « Maintenance » à droite des boutons du bas de l'écran.</summary>
        private static void AddButton(Form f)
        {
            Item cancel = f.Items.Item("2");
            int left = cancel.Left + cancel.Width + 5;
            for (int i = 0; i < f.Items.Count; i++)
            {
                Item it = f.Items.Item(i);
                if (it.Visible && it.UniqueID != Button && Math.Abs(it.Top - cancel.Top) <= 3 && it.Left >= left)
                    left = Math.Max(left, it.Left + it.Width + 5);
            }
            Item b = f.Items.Add(Button, BoFormItemTypes.it_BUTTON);
            b.Left = left;
            b.Top = cancel.Top;
            b.Width = 100;
            b.Height = cancel.Height;
            b.AffectsFormMode = false;
            ((SAPbouiCOM.Button)b.Specific).Caption = "Maintenance";
        }

        private static int DocEntry(Form f)
        {
            int.TryParse(f.DataSources.DBDataSources.Item("OWOR").GetValue("DocEntry", 0).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int entry);
            return entry;
        }

        private static void OpenView(Form f)
        {
            int entry = f.Mode == BoFormMode.fm_ADD_MODE ? 0 : DocEntry(f);
            if (entry <= 0)
            {
                Program.Message(_app, "Enregistrez d'abord l'ordre de fabrication.");
                return;
            }
            Navigator.Production.Show(entry);
        }

        /// <summary>
        /// Enregistrement de l'OF : la ligne saisie doit être une ligne de production de la
        /// maintenance ; au passage au statut Lancé, liste des points à vérifier sur les machines
        /// de la ligne, l'utilisateur confirme ou renonce (false bloque l'enregistrement).
        /// </summary>
        private static bool CheckSave(Form f)
        {
            if (f.Mode != BoFormMode.fm_ADD_MODE && f.Mode != BoFormMode.fm_UPDATE_MODE)
                return true;
            DBDataSource ds = f.DataSources.DBDataSources.Item("OWOR");
            string typed = ds.GetValue("U_MNT_PLine", 0).Trim();
            if (typed != "" && !ProductionService.IsLine(typed))
            {
                _app.StatusBar.SetText("Ligne de production (maint.) : « " + typed + " » n'est pas une ligne de production de la maintenance " +
                                       "(poste technique avec la case « Ligne de production »).", BoMessageTime.bmt_Medium, BoStatusBarMessageType.smt_Error);
                return false;
            }
            if (ds.GetValue("Status", 0).Trim() != ProdStatus.Released)
                return true;
            int entry = f.Mode == BoFormMode.fm_ADD_MODE ? 0 : DocEntry(f);
            if (entry > 0 && Sql.ScalarStr("SELECT \"Status\" FROM \"OWOR\" WHERE \"DocEntry\" = " + entry) == ProdStatus.Released)
                return true;
            string line = ds.GetValue("U_MNT_PLine", 0).Trim();
            if (line == "")
                line = Sql.ScalarStr("SELECT \"U_MNT_PLine\" FROM \"OITM\" WHERE \"ItemCode\" = " + Sql.Q(ds.GetValue("ItemCode", 0).Trim()));
            List<string> warnings = ProductionService.ReleaseWarnings(line);
            if (warnings.Count == 0)
                return true;
            if (warnings.Count > 12)
                warnings = warnings.GetRange(0, 12);
            return Program.Message(_app, "Avant de lancer la production sur la ligne " + line + " :\n- " + string.Join("\n- ", warnings) +
                                         "\n\nLancer l'ordre de fabrication quand même ?", true) == 1;
        }
    }
}
