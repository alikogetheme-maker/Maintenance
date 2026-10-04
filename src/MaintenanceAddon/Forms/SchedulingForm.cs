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
    /// Ordonnancement des plans (IP24 vue d'ensemble + IP30 appel en masse) :
    /// échéances dans l'horizon, création des ordres préventifs, sauts d'échéance.
    /// </summary>
    internal sealed class SchedulingForm : SimpleForm
    {
        private const string Dt = "dtSched";
        private List<PlanDue> _rows = new List<PlanDue>();
        private int _selected = -1;

        public SchedulingForm(Application app) : base(app) { }

        protected override string FormType => FormIds.SchedForm;
        protected override string Title => "Ordonnancement des plans de maintenance";
        protected override int FormWidth => 960;
        protected override int FormHeight => 560;

        public void Show()
        {
            if (Open())
                Refresh();
        }

        protected override void Build()
        {
            AddUds("udHor", BoDataType.dt_DATE);
            U.Label("lHor", "Échéances jusqu'au", 10, 10, 120, "eHor");
            U.EditUds("eHor", 130, 10, 100, "udHor");
            SetUds("udHor", DateValue(DateTime.Today.AddDays(SettingsService.Load().HorizonDays)));
            U.Button("bRefresh", "Actualiser", 240, 8, 100);
            U.Label("lInfo", "Un plan est « à appeler » quand son échéance moins l'horizon d'appel tombe avant cette date (ou quand le compteur l'atteint).", 350, 10, 590);

            DataTable dt = F.DataSources.DataTables.Add(Dt);
            dt.Columns.Add("Plan", BoFieldsType.ft_AlphaNumeric, 50);
            dt.Columns.Add("Desig", BoFieldsType.ft_AlphaNumeric, 100);
            dt.Columns.Add("Type", BoFieldsType.ft_AlphaNumeric, 30);
            dt.Columns.Add("Base", BoFieldsType.ft_AlphaNumeric, 30);
            dt.Columns.Add("Equip", BoFieldsType.ft_AlphaNumeric, 50);
            dt.Columns.Add("Echeance", BoFieldsType.ft_Date);
            dt.Columns.Add("EchCpt", BoFieldsType.ft_Float);
            dt.Columns.Add("CptAct", BoFieldsType.ft_Float);
            dt.Columns.Add("Etat", BoFieldsType.ft_AlphaNumeric, 60);

            U.Grid("gSched", Dt, 10, 35, FormWidth - 30, FormHeight - 115);
            U.Button("bCall", "Appeler le plan sélectionné", 10, FormHeight - 62, 180);
            U.Button("bAll", "Appeler tous les plans dus", 195, FormHeight - 62, 180);
            U.Button("bSkip", "Ignorer l'échéance", 380, FormHeight - 62, 140);
            U.Button("bOpen", "Ouvrir le plan", 525, FormHeight - 62, 110);
            U.Button("bClose", "Fermer", 640, FormHeight - 62, 90);
        }

        private void Refresh()
        {
            DateTime horizon = Sql.ParseDate(Uds("udHor")) ?? DateTime.Today;
            _rows = PlanService.Overview(horizon);
            _selected = -1;

            DataTable dt = F.DataSources.DataTables.Item(Dt);
            dt.Rows.Clear();
            if (_rows.Count > 0)
                dt.Rows.Add(_rows.Count);
            for (int i = 0; i < _rows.Count; i++)
            {
                PlanDue d = _rows[i];
                dt.SetValue("Plan", i, d.PlanCode);
                dt.SetValue("Desig", i, d.PlanName);
                dt.SetValue("Type", i, PlanTypes.List.Caption(d.Type));
                dt.SetValue("Base", i, SchedBasis.List.Caption(d.Basis));
                dt.SetValue("Equip", i, d.Equip);
                if (d.DueDate.HasValue)
                    dt.SetValue("Echeance", i, d.DueDate.Value);
                dt.SetValue("EchCpt", i, d.DueCounter);
                dt.SetValue("CptAct", i, d.CurrentCounter);
                dt.SetValue("Etat", i, d.State);
            }

            F.Freeze(true);
            try
            {
                Grid grid = (Grid)F.Items.Item("gSched").Specific;
                grid.DataTable = dt;
                var captions = new Dictionary<string, string>
                {
                    { "Plan", "Plan" }, { "Desig", "Désignation" }, { "Type", "Type" }, { "Base", "Échéance suivante depuis" },
                    { "Equip", "Équipement" }, { "Echeance", "Échéance (estimée si compteur)" }, { "EchCpt", "Échéance compteur" },
                    { "CptAct", "Compteur actuel" }, { "Etat", "État" }
                };
                for (int i = 0; i < grid.Columns.Count; i++)
                {
                    GridColumn col = grid.Columns.Item(i);
                    col.Editable = false;
                    if (captions.TryGetValue(col.UniqueID, out string c))
                        col.TitleObject.Caption = c;
                }
                grid.AutoResizeColumns();
            }
            finally
            {
                F.Freeze(false);
            }
            int due = _rows.FindAll(r => r.IsDue).Count;
            Msg(_rows.Count + " plan(s) actif(s), dont " + due + " à appeler.");
        }

        protected override void OnEvent(ItemEvent e)
        {
            if (e.EventType == BoEventTypes.et_CLICK && e.ItemUID == "gSched" && e.Row >= 0)
            {
                Grid grid = (Grid)F.Items.Item("gSched").Specific;
                _selected = grid.GetDataTableRowIndex(e.Row);
                return;
            }
            if (e.EventType == BoEventTypes.et_DOUBLE_CLICK && e.ItemUID == "gSched" && e.Row >= 0)
            {
                Grid grid = (Grid)F.Items.Item("gSched").Specific;
                int idx = grid.GetDataTableRowIndex(e.Row);
                if (idx >= 0 && idx < _rows.Count)
                    Navigator.Open(Obj.Plan, _rows[idx].PlanCode);
                return;
            }
            if (e.EventType != BoEventTypes.et_ITEM_PRESSED || !e.ActionSuccess)
                return;

            switch (e.ItemUID)
            {
                case "bRefresh": Refresh(); break;
                case "bClose": Close(); break;
                case "bOpen":
                    if (Selected() != null)
                        Navigator.Open(Obj.Plan, Selected().PlanCode);
                    break;
                case "bCall": CallSelected(); break;
                case "bAll": CallAll(); break;
                case "bSkip": SkipSelected(); break;
            }
        }

        private PlanDue Selected()
        {
            if (_selected < 0 || _selected >= _rows.Count)
            {
                Msg("Cliquez d'abord sur une ligne de la liste.", BoStatusBarMessageType.smt_Warning);
                return null;
            }
            return _rows[_selected];
        }

        private void CallSelected()
        {
            PlanDue d = Selected();
            if (d == null)
                return;
            string question = d.IsDue
                ? "Créer l'ordre préventif du plan " + d.PlanCode + " ?"
                : "Le plan " + d.PlanCode + " n'est pas encore dû (" + d.State + "). Créer quand même l'ordre ?";
            if (!Confirm(question))
                return;
            int order = PlanService.Call(d.PlanCode);
            Refresh();
            Msg("Ordre " + OrderService.DocNum(order) + " créé pour le plan " + d.PlanCode + ".");
        }

        private void CallAll()
        {
            List<PlanDue> due = _rows.FindAll(r => r.IsDue);
            if (due.Count == 0)
            {
                Msg("Aucun plan à appeler dans l'horizon.", BoStatusBarMessageType.smt_Warning);
                return;
            }
            if (!Confirm("Créer les ordres préventifs de " + due.Count + " plan(s) ?"))
                return;

            var created = new List<string>();
            var errors = new List<string>();
            foreach (PlanDue d in due)
            {
                try
                {
                    created.Add(d.PlanCode + " → ordre " + OrderService.DocNum(PlanService.Call(d.PlanCode)));
                }
                catch (Exception ex)
                {
                    errors.Add(d.PlanCode + " : " + ex.Message);
                    Program.Log("Appel du plan " + d.PlanCode + " : " + ex);
                }
            }
            Refresh();
            Program.Message(App, created.Count + " ordre(s) créé(s)." +
                           (created.Count > 0 ? "\n" + string.Join("\n", created) : "") +
                           (errors.Count > 0 ? "\n\nErreurs :\n" + string.Join("\n", errors) : ""));
        }

        private void SkipSelected()
        {
            PlanDue d = Selected();
            if (d == null)
                return;
            if (!Confirm("Ignorer l'échéance actuelle du plan " + d.PlanCode + " ? Aucun ordre ne sera créé et l'échéance suivante sera calculée."))
                return;
            PlanService.Skip(d.PlanCode);
            Refresh();
            Msg("Échéance du plan " + d.PlanCode + " ignorée.");
        }
    }
}
