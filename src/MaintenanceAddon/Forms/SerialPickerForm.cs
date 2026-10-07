using System;
using System.Globalization;
using SAPbouiCOM;
using MaintenanceAddon.Core;
using MaintenanceAddon.Services;

namespace MaintenanceAddon.Forms
{
    /// <summary>
    /// Choix d'un n° de série reçu dans SAP (réception d'achat, facture, entrée de
    /// marchandises) pour créer ou compléter une fiche équipement.
    /// </summary>
    internal sealed class SerialPickerForm : SimpleForm
    {
        private const string Dt = "dtSer";
        private Action<SerialInfo> _onChosen;
        private string _item = "";

        public SerialPickerForm(Application app) : base(app) { }

        protected override string FormType => FormIds.SerialForm;
        protected override string Title => "N° de série reçus dans SAP";
        protected override int FormWidth => 900;
        protected override int FormHeight => 460;

        public void Show(string itemCode, Action<SerialInfo> onChosen)
        {
            _onChosen = onChosen;
            _item = itemCode ?? "";
            if (!Open(true))
                SetUds("udItem", _item);
            Search();
        }

        protected override void Build()
        {
            AddUds("udItem", BoDataType.dt_SHORT_TEXT, 50);
            AddUds("udText", BoDataType.dt_SHORT_TEXT, 100);
            AddUds("udFree", BoDataType.dt_SHORT_TEXT, 1);
            ChooseFromList cfl = U.Cfl("cflItem", "4");
            Ui.CflFilter(cfl, "ManSerNum", "Y");
            U.Label("lItem", "Article", 10, 10, 60, "eItem");
            Ui.BindCfl(U.EditUds("eItem", 70, 10, 120, "udItem"), "cflItem", "ItemCode");
            U.Label("lText", "N° ou désignation", 205, 10, 110, "eText");
            U.EditUds("eText", 315, 10, 160, "udText");
            U.CheckUds("cFree", "Seulement les n° sans équipement", 490, 10, 220, "udFree");
            U.Button("bSearch", "Rechercher", 720, 8, 90);
            U.Grid("gSer", Dt, 10, 35, FormWidth - 40, FormHeight - 110);
            U.Label("lInfo", "Double-cliquez sur une ligne ou sélectionnez-la puis « Choisir » : la fiche équipement est complétée.", 250, FormHeight - 60, 600);
            U.Button("bChoose", "Choisir", 10, FormHeight - 62, 100);
            U.Button("bCancel", "Annuler", 115, FormHeight - 62, 90);
            SetUds("udItem", _item);
            SetUds("udFree", "Y");
        }

        private void Search()
        {
            DataTable dt = U.LoadGrid("gSer", Dt, EquipmentService.ReceivedSerialsSql(Uds("udItem").Trim(), Uds("udText"), Uds("udFree") == "Y"));
            Msg(Ui.IsEmpty(dt) ? "Aucun n° de série reçu ne correspond." : dt.Rows.Count + " n° de série.",
                Ui.IsEmpty(dt) ? BoStatusBarMessageType.smt_Warning : BoStatusBarMessageType.smt_Success);
        }

        protected override void OnEvent(ItemEvent e)
        {
            switch (e.EventType)
            {
                case BoEventTypes.et_CHOOSE_FROM_LIST:
                    string value = Chosen(e, "ItemCode");
                    if (value != null)
                    {
                        SetUds("udItem", value);
                        Search();
                    }
                    return;
                case BoEventTypes.et_DOUBLE_CLICK:
                    if (e.ItemUID == "gSer" && e.Row >= 0)
                        Choose(e.Row);
                    return;
                case BoEventTypes.et_ITEM_PRESSED:
                    if (!e.ActionSuccess)
                        return;
                    if (e.ItemUID == "bSearch")
                        Search();
                    else if (e.ItemUID == "bCancel")
                        Close();
                    else if (e.ItemUID == "bChoose")
                    {
                        Grid g = (Grid)F.Items.Item("gSer").Specific;
                        if (g.Rows.SelectedRows.Count == 0)
                        {
                            Msg("Sélectionnez d'abord une ligne.", BoStatusBarMessageType.smt_Warning);
                            return;
                        }
                        Choose(g.Rows.SelectedRows.Item(0, BoOrderType.ot_RowOrder));
                    }
                    return;
            }
        }

        /// <summary>Ligne de grille choisie (index de grille).</summary>
        internal void Choose(int gridRow)
        {
            Grid g = (Grid)F.Items.Item("gSer").Specific;
            DataTable dt = F.DataSources.DataTables.Item(Dt);
            if (Ui.IsEmpty(dt))
                return;
            int row = g.GetDataTableRowIndex(gridRow);
            if (row < 0 || row >= dt.Rows.Count)
                return;
            string item = Convert.ToString(dt.GetValue("Article", row), CultureInfo.InvariantCulture).Trim();
            string serial = Convert.ToString(dt.GetValue("Serie", row), CultureInfo.InvariantCulture).Trim();
            SerialInfo info = EquipmentService.Serial(item, serial);
            if (info == null)
                return;
            Action<SerialInfo> callback = _onChosen;
            Close();
            callback?.Invoke(info);
        }
    }
}
