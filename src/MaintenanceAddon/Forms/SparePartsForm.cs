using System;
using System.Collections.Generic;
using SAPbouiCOM;
using MaintenanceAddon.Core;
using MaintenanceAddon.Models;
using MaintenanceAddon.Services;

namespace MaintenanceAddon.Forms
{
    /// <summary>
    /// Pièces de rechange de l'équipement proposées pendant la préparation d'un
    /// ordre : l'utilisateur saisit les quantités à prévoir, les lignes sont
    /// ajoutées aux composants de l'ordre.
    /// </summary>
    internal sealed class SparePartsForm : SimpleForm
    {
        private const string Dt = "dtSpr";
        private string _equip = "";
        private string _whs = "";
        private Action<List<CompDraft>> _onAdd;
        private readonly List<Row> _parts = new List<Row>();

        public SparePartsForm(Application app) : base(app) { }

        protected override string FormType => FormIds.SparePartsForm;
        protected override string Title => "Pièces de rechange de l'équipement";
        protected override int FormWidth => 760;
        protected override int FormHeight => 400;

        public void Show(string equip, string whs, Action<List<CompDraft>> onAdd)
        {
            if (F != null)
                Close();
            _equip = equip ?? "";
            _whs = whs ?? "";
            _onAdd = onAdd;
            _parts.Clear();
            _parts.AddRange(EquipmentService.SpareParts(_equip, _whs));
            if (_parts.Count == 0)
            {
                Program.Message(App, "Aucune pièce de rechange n'est renseignée pour l'équipement " + _equip +
                                     ".\nComplétez l'onglet « Pièces de rechange » de sa fiche.");
                return;
            }
            Open(true);
        }

        protected override void Build()
        {
            F.Title = "Pièces de rechange - " + _equip + " " + NotificationService.NameOf(Db.Equip, _equip);
            U.Label("lInfo", "Saisissez la quantité à prévoir sur l'ordre (0 = pièce non retenue). Disponible : magasin " + _whs + ".", 10, 10, 700);

            DataTable dt = F.DataSources.DataTables.Add(Dt);
            dt.Columns.Add("Item", BoFieldsType.ft_AlphaNumeric, 50);
            dt.Columns.Add("Name", BoFieldsType.ft_AlphaNumeric, 100);
            dt.Columns.Add("Mount", BoFieldsType.ft_Quantity);
            dt.Columns.Add("Avail", BoFieldsType.ft_Quantity);
            dt.Columns.Add("Qty", BoFieldsType.ft_Quantity);
            dt.Rows.Add(_parts.Count);
            for (int i = 0; i < _parts.Count; i++)
            {
                dt.SetValue("Item", i, _parts[i].Str("U_ItemCode"));
                dt.SetValue("Name", i, _parts[i].Str("U_ItemName"));
                dt.SetValue("Mount", i, _parts[i].Dbl("U_Qty"));
                dt.SetValue("Avail", i, _parts[i].Dbl("Dispo"));
                dt.SetValue("Qty", i, 0);
            }

            Matrix m = U.Matrix("mSpr", 10, 35, FormWidth - 40, FormHeight - 115);
            m.SelectionMode = BoMatrixSelect.ms_None;
            AddColumn(m, "cItem", "Article", 120, "Item", false);
            AddColumn(m, "cName", "Désignation", 270, "Name", false);
            AddColumn(m, "cMount", "Qté montée", 80, "Mount", false);
            AddColumn(m, "cAvail", "Disponible", 80, "Avail", false);
            AddColumn(m, "cQty", "Qté à prévoir", 90, "Qty", true);
            m.LoadFromDataSource();

            U.Button("bAdd", "Ajouter à l'ordre", 10, FormHeight - 62, 130);
            U.Button("bCancel", "Annuler", 145, FormHeight - 62, 90);
        }

        private static void AddColumn(Matrix m, string id, string caption, int width, string column, bool editable)
        {
            Column c = m.Columns.Add(id, BoFormItemTypes.it_EDIT);
            c.TitleObject.Caption = caption;
            c.Width = width;
            c.Editable = editable;
            c.DataBind.Bind(Dt, column);
        }

        protected override void OnEvent(ItemEvent e)
        {
            if (e.EventType != BoEventTypes.et_ITEM_PRESSED || !e.ActionSuccess)
                return;
            if (e.ItemUID == "bCancel")
                Close();
            else if (e.ItemUID == "bAdd")
                Add();
        }

        private void Add()
        {
            Matrix m = (Matrix)F.Items.Item("mSpr").Specific;
            var list = new List<CompDraft>();
            for (int row = 1; row <= m.RowCount; row++)
            {
                double qty = Sql.ParseDouble(((EditText)m.Columns.Item("cQty").Cells.Item(row).Specific).Value);
                if (qty < 0)
                {
                    Program.Message(App, "Ligne " + row + " : quantité négative.");
                    return;
                }
                if (qty == 0)
                    continue;
                Row p = _parts[row - 1];
                list.Add(new CompDraft
                {
                    ItemCode = p.Str("U_ItemCode"),
                    ItemName = p.Str("U_ItemName"),
                    Qty = qty,
                    Whs = _whs,
                    Proc = p.Str("InvntItem") == "Y" ? Procurement.Stock : Procurement.Purchase
                });
            }
            if (list.Count == 0)
            {
                Msg("Saisissez au moins une quantité.", BoStatusBarMessageType.smt_Warning);
                return;
            }
            Action<List<CompDraft>> callback = _onAdd;
            Close();
            callback?.Invoke(list);
        }
    }
}
