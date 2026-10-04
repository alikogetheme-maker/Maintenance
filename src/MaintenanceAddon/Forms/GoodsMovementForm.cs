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
    /// Sortie (MIGO 261) ou retour (262) des composants d'un ordre : une
    /// sortie / entrée de stock SAP imputée en charge sur l'ordre.
    /// </summary>
    internal sealed class GoodsMovementForm : SimpleForm
    {
        private const string Dt = "dtGds";
        private int _order;
        private bool _issue;
        private Action _onSaved;
        private readonly List<Row> _lines = new List<Row>();

        public GoodsMovementForm(Application app) : base(app) { }

        protected override string FormType => FormIds.GoodsForm;
        protected override string Title => "Mouvement de pièces";
        protected override int FormWidth => 820;
        protected override int FormHeight => 420;

        public void Show(int orderEntry, bool issue, Action onSaved)
        {
            if (F != null)
                Close();
            _order = orderEntry;
            _issue = issue;
            _onSaved = onSaved;

            _lines.Clear();
            foreach (Row r in Sql.Rows("SELECT \"LineId\", \"U_ItemCode\", \"U_ItemName\", \"U_Whs\", \"U_Qty\", \"U_Proc\" FROM " + Db.T(Db.OrderComps) +
                                       " WHERE \"DocEntry\" = " + orderEntry + " ORDER BY \"LineId\""))
            {
                r["Issued"] = OrderService.IssuedQty(orderEntry, r.Int("LineId"));
                bool stocked = Sql.ScalarStr("SELECT \"InvntItem\" FROM \"OITM\" WHERE \"ItemCode\" = " + Sql.Q(r.Str("U_ItemCode"))) == "Y";
                if (!stocked)
                    continue;
                if (!issue && r.Dbl("Issued") <= 0)
                    continue;
                _lines.Add(r);
            }
            if (_lines.Count == 0)
            {
                Program.Message(App, issue ? "Aucun composant géré en stock sur cet ordre." : "Aucune pièce sortie à retourner sur cet ordre.");
                return;
            }
            Open(true);
        }

        protected override void Build()
        {
            F.Title = (_issue ? "Sortie de pièces" : "Retour de pièces") + " - ordre " + OrderService.DocNum(_order);
            AddUds("udDate", BoDataType.dt_DATE);
            U.Label("lDate", "Date de comptabilisation", 10, 10, 150, "eDate");
            U.EditUds("eDate", 160, 10, 100, "udDate");
            SetUds("udDate", DateValue(DateTime.Today));
            U.Label("lInfo", _issue
                ? "Quantités proposées : reste à sortir. Mettez 0 pour ne pas sortir une ligne."
                : "Saisissez les quantités non utilisées à remettre en stock (valorisées au coût de sortie).", 280, 10, 520);

            DataTable dt = F.DataSources.DataTables.Add(Dt);
            dt.Columns.Add("Line", BoFieldsType.ft_Integer);
            dt.Columns.Add("Item", BoFieldsType.ft_AlphaNumeric, 50);
            dt.Columns.Add("Name", BoFieldsType.ft_AlphaNumeric, 100);
            dt.Columns.Add("Whs", BoFieldsType.ft_AlphaNumeric, 8);
            dt.Columns.Add("Plan", BoFieldsType.ft_Quantity);
            dt.Columns.Add("Iss", BoFieldsType.ft_Quantity);
            dt.Columns.Add("Qty", BoFieldsType.ft_Quantity);
            dt.Rows.Add(_lines.Count);
            for (int i = 0; i < _lines.Count; i++)
            {
                Row r = _lines[i];
                double planned = r.Dbl("U_Qty"), issued = r.Dbl("Issued");
                dt.SetValue("Line", i, r.Int("LineId"));
                dt.SetValue("Item", i, r.Str("U_ItemCode"));
                dt.SetValue("Name", i, r.Str("U_ItemName"));
                dt.SetValue("Whs", i, r.Str("U_Whs"));
                dt.SetValue("Plan", i, planned);
                dt.SetValue("Iss", i, issued);
                dt.SetValue("Qty", i, _issue ? (r.Str("U_Proc") == Procurement.Stock ? Math.Max(0, planned - issued) : 0) : 0);
            }

            Matrix m = U.Matrix("mGds", 10, 35, FormWidth - 40, FormHeight - 115);
            m.SelectionMode = BoMatrixSelect.ms_None;
            AddColumn(m, "cLine", "#", 30, "Line", false);
            AddColumn(m, "cItem", "Article", 110, "Item", false);
            AddColumn(m, "cName", "Désignation", 230, "Name", false);
            AddColumn(m, "cWhs", "Magasin", 70, "Whs", true);
            AddColumn(m, "cPlan", "Qté prévue", 80, "Plan", false);
            AddColumn(m, "cIss", "Déjà sortie", 80, "Iss", false);
            AddColumn(m, "cQty", _issue ? "Qté à sortir" : "Qté à retourner", 100, "Qty", true);
            m.LoadFromDataSource();

            U.Button("bOk", _issue ? "Sortir du stock" : "Remettre en stock", 10, FormHeight - 62, 130);
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
            else if (e.ItemUID == "bOk")
                Post();
        }

        private void Post()
        {
            DateTime? date = Sql.ParseDate(Uds("udDate"));
            if (date == null)
            {
                Msg("Saisissez la date de comptabilisation.", BoStatusBarMessageType.smt_Warning);
                return;
            }

            // Quantités et magasins lus directement dans les cellules affichées
            Matrix m = (Matrix)F.Items.Item("mGds").Specific;
            var moves = new List<Movement>();
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
                moves.Add(new Movement
                {
                    LineId = _lines[row - 1].Int("LineId"),
                    Qty = qty,
                    Whs = ((EditText)m.Columns.Item("cWhs").Cells.Item(row).Specific).Value.Trim()
                });
            }
            if (moves.Count == 0)
            {
                Msg("Saisissez au moins une quantité.", BoStatusBarMessageType.smt_Warning);
                return;
            }
            if (!Confirm((_issue ? "Sortir du stock " : "Remettre en stock ") + moves.Count + " ligne(s) pour l'ordre " + OrderService.DocNum(_order) + " ?"))
                return;

            string docNum = _issue
                ? OrderService.IssueComponents(_order, moves, date.Value)
                : OrderService.ReturnComponents(_order, moves, date.Value);
            Close();
            Program.Message(App, (_issue ? "Sortie de stock n° " : "Entrée de stock n° ") + docNum + " créée et imputée sur l'ordre.");
            try
            {
                _onSaved?.Invoke();
            }
            catch (Exception ex)
            {
                Program.Message(App, "Le mouvement est enregistré, mais l'ordre n'a pas pu être actualisé : " + ex.Message);
            }
        }
    }
}
