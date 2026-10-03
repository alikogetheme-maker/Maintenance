using System;
using System.Collections.Generic;
using System.Globalization;
using SAPbouiCOM;
using MaintenanceAddon.Core;
using MaintenanceAddon.Services;

namespace MaintenanceAddon.Forms
{
    /// <summary>
    /// Rapports de maintenance : une fenêtre, plusieurs vues (liste des ordres,
    /// avis, backlog, structure, indicateurs, coûts...). Double-clic ou flèche :
    /// ouverture de l'objet.
    /// </summary>
    internal sealed class ListForm : SimpleForm
    {
        private const string Dt = "dtList";
        public static ListForm Instance { get; private set; }

        public ListForm(Application app) : base(app)
        {
            Instance = this;
        }

        protected override string FormType => FormIds.ListForm;
        protected override string Title => "Rapports de maintenance";
        protected override int FormWidth => 1000;
        protected override int FormHeight => 600;

        public void Show(string view, string equip)
        {
            bool created = Open();
            if (!string.IsNullOrEmpty(view))
                SetUds("udView", ReportService.View(view).Code);
            if (equip != null)
                SetUds("udEq", equip);
            ApplyView();
            if (created || !string.IsNullOrEmpty(view))
                Search();
        }

        protected override void Build()
        {
            AddUds("udView", BoDataType.dt_SHORT_TEXT, 3);
            AddUds("udFrom", BoDataType.dt_DATE);
            AddUds("udTo", BoDataType.dt_DATE);
            AddUds("udEq", BoDataType.dt_SHORT_TEXT, 50);
            AddUds("udSt", BoDataType.dt_SHORT_TEXT, 4);
            AddUds("udType", BoDataType.dt_SHORT_TEXT, 4);
            AddUds("udWc", BoDataType.dt_SHORT_TEXT, 50);
            AddUds("udTot", BoDataType.dt_LONG_TEXT, 254);

            U.Label("lView", "Rapport", 10, 10, 60, "cView");
            ComboBox view = U.ComboUds("cView", 70, 10, 330, "udView");
            foreach (ReportView v in ReportService.Views)
                view.ValidValues.Add(v.Code, v.Title);
            SetUds("udView", ReportService.Views[0].Code);

            U.Label("lFrom", "Du", 420, 10, 25, "eFrom");
            U.EditUds("eFrom", 445, 10, 90, "udFrom");
            U.Label("lTo", "au", 545, 10, 25, "eTo");
            U.EditUds("eTo", 570, 10, 90, "udTo");
            SetUds("udFrom", DateValue(new DateTime(DateTime.Today.Year, 1, 1)));
            SetUds("udTo", DateValue(DateTime.Today.AddMonths(3)));

            U.Cfl("cflEq", Obj.Equip);
            U.Label("lEq", "Équipement", 10, 30, 60, "eEq");
            Ui.BindCfl(U.EditUds("eEq", 70, 30, 120, "udEq"), "cflEq", "Code");
            U.Label("lSt", "Statut", 200, 30, 40, "cSt");
            U.ComboUds("cSt", 240, 30, 160, "udSt");
            U.Label("lType", "Type", 420, 30, 35, "cType");
            U.ComboUds("cType", 455, 30, 205, "udType");
            U.Cfl("cflWc", Obj.WorkCtr);
            U.Label("lWc", "Poste trav.", 670, 30, 65, "eWc");
            Ui.BindCfl(U.EditUds("eWc", 735, 30, 90, "udWc"), "cflWc", "Code");
            U.Button("bSearch", "Rechercher", 840, 28, 130);

            U.Grid("gList", Dt, 10, 55, FormWidth - 30, FormHeight - 155);
            U.ReadOnlyUds("eTot", 10, FormHeight - 95, FormWidth - 30, "udTot");
            U.Button("bClose", "Fermer", 10, FormHeight - 62, 90);
            U.Label("lHelp", "Double-clic sur une ligne (ou flèche) pour ouvrir l'ordre, l'avis, l'équipement...", 110, FormHeight - 60, 600);
            ApplyView();
        }

        private ReportView CurrentView => ReportService.View(Uds("udView"));

        /// <summary>Filtres Statut / Type selon la vue.</summary>
        private void ApplyView()
        {
            ReportView v = CurrentView;
            ComboBox st = (ComboBox)F.Items.Item("cSt").Specific;
            ComboBox ty = (ComboBox)F.Items.Item("cType").Specific;
            string oldSt = Uds("udSt"), oldTy = Uds("udType");
            SetUds("udSt", "");
            SetUds("udType", "");
            Ui.Fill(st, v.Statuses, true);
            Ui.Fill(ty, v.Types, true);
            if (v.Statuses != null && v.Statuses.Caption(oldSt) != oldSt)
                SetUds("udSt", oldSt);
            if (v.Types != null && v.Types.Caption(oldTy) != oldTy)
                SetUds("udType", oldTy);
            F.Items.Item("cSt").Enabled = v.Statuses != null;
            F.Items.Item("cType").Enabled = v.Types != null;
            F.Items.Item("eFrom").Enabled = v.UsesDates;
            F.Items.Item("eTo").Enabled = v.UsesDates;
        }

        private void Search()
        {
            ReportView v = CurrentView;
            DateTime from = Sql.ParseDate(Uds("udFrom")) ?? new DateTime(DateTime.Today.Year, 1, 1);
            DateTime to = Sql.ParseDate(Uds("udTo")) ?? DateTime.Today;
            if (from > to)
            {
                Msg("La date de début doit précéder la date de fin.", BoStatusBarMessageType.smt_Warning);
                return;
            }
            var filter = new ReportFilter
            {
                From = from,
                To = to,
                Equip = Uds("udEq").Trim(),
                Status = Uds("udSt").Trim(),
                Type = Uds("udType").Trim(),
                WorkCtr = Uds("udWc").Trim()
            };

            DataTable dt;
            F.Freeze(true);
            try
            {
                F.Title = "Rapports de maintenance - " + v.Title;
                dt = U.LoadGrid("gList", Dt, v.Sql(filter));
                Grid grid = (Grid)F.Items.Item("gList").Specific;
                for (int i = 0; i < grid.Columns.Count; i++)
                {
                    GridColumn col = grid.Columns.Item(i);
                    string obj = LinkObject(v, col.UniqueID);
                    if (obj == null)
                        continue;
                    col.Type = BoGridColumnType.gct_EditText;
                    ((EditTextColumn)col).LinkedObjectType = obj;
                }
            }
            finally
            {
                F.Freeze(false);
            }
            ShowTotals(v, dt);
        }

        private static string LinkObject(ReportView v, string column)
        {
            if (column == "Num" && v.NumObject != null)
                return v.NumObject;
            return v.Links.TryGetValue(column, out string obj) ? obj : null;
        }

        private void ShowTotals(ReportView v, DataTable dt)
        {
            int count = Ui.IsEmpty(dt) ? 0 : dt.Rows.Count;
            var parts = new List<string> { count + " ligne(s)" };
            if (count > 0)
            {
                var fr = CultureInfo.GetCultureInfo("fr-FR");
                foreach (string col in v.Totals)
                {
                    double sum = 0;
                    for (int i = 0; i < count; i++)
                        sum += ToDouble(dt.GetValue(col, i));
                    string caption = ReportService.Captions.TryGetValue(col, out string c) ? c : col;
                    parts.Add(caption + " : " + sum.ToString("N2", fr));
                }
            }
            SetUds("udTot", string.Join("   |   ", parts));
            Msg(count + " ligne(s).");
        }

        private static double ToDouble(object value)
        {
            if (value == null || value is DBNull)
                return 0;
            if (value is string s)
                return Sql.ParseDouble(s);
            try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); } catch { return 0; }
        }

        protected override bool OnBefore(ItemEvent e)
        {
            // Flèche de lien : on ouvre nos écrans au lieu de la fenêtre par défaut SAP
            if (e.EventType == BoEventTypes.et_MATRIX_LINK_PRESSED && e.ItemUID == "gList")
            {
                OpenLink(e.ColUID, e.Row);
                return false;
            }
            return true;
        }

        private void OpenLink(string column, int gridRow)
        {
            ReportView v = CurrentView;
            string obj = LinkObject(v, column);
            if (obj == null)
                return;
            Grid grid = (Grid)F.Items.Item("gList").Specific;
            DataTable dt = F.DataSources.DataTables.Item(Dt);
            int row = grid.GetDataTableRowIndex(gridRow);
            if (row < 0 || Ui.IsEmpty(dt) || row >= dt.Rows.Count)
                return;
            string key = Convert.ToString(dt.GetValue(column == "Num" ? "Key" : column, row), CultureInfo.InvariantCulture).Trim();
            if (key != "" && key != "0")
                Navigator.Open(obj, key);
        }

        protected override void OnEvent(ItemEvent e)
        {
            switch (e.EventType)
            {
                case BoEventTypes.et_COMBO_SELECT:
                    if (e.ItemUID == "cView")
                    {
                        ApplyView();
                        Search();
                    }
                    break;
                case BoEventTypes.et_CHOOSE_FROM_LIST:
                    {
                        string code = Chosen(e, "Code");
                        if (code == null)
                            break;
                        if (e.ItemUID == "eEq") SetUds("udEq", code);
                        else if (e.ItemUID == "eWc") SetUds("udWc", code);
                        break;
                    }
                case BoEventTypes.et_DOUBLE_CLICK:
                    if (e.ItemUID == "gList" && e.Row >= 0)
                    {
                        // Objet de la ligne : colonne N° (ordre / avis), KeyObj (structure), sinon l'équipement
                        ReportView v = CurrentView;
                        if (v.NumObject != null || v.HasKeyObj)
                            UdoForm.OpenGridRow(F, "gList", Dt, v.NumObject, e.Row);
                        else if (v.Links.ContainsKey("Equip"))
                            OpenLink("Equip", e.Row);
                    }
                    break;
                case BoEventTypes.et_ITEM_PRESSED:
                    if (!e.ActionSuccess)
                        break;
                    if (e.ItemUID == "bSearch")
                        Search();
                    else if (e.ItemUID == "bClose")
                        Close();
                    break;
            }
        }
    }
}
