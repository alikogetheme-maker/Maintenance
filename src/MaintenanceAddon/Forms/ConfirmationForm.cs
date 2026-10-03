using System;
using System.Collections.Generic;
using System.Globalization;
using SAPbouiCOM;
using MaintenanceAddon.Core;
using MaintenanceAddon.Services;

namespace MaintenanceAddon.Forms
{
    /// <summary>Confirmation de temps sur une opération d'ordre (IW41).</summary>
    internal sealed class ConfirmationForm : SimpleForm
    {
        private int _order;
        private Action _onSaved;
        private readonly Dictionary<string, Row> _ops = new Dictionary<string, Row>();

        public ConfirmationForm(Application app) : base(app) { }

        protected override string FormType => FormIds.ConfForm;
        protected override string Title => "Confirmation de temps";
        protected override int FormWidth => 520;
        protected override int FormHeight => 330;

        public void Show(int orderEntry, int opLineId, Action onSaved)
        {
            if (F != null && _order != orderEntry)
                Close();
            _order = orderEntry;
            _onSaved = onSaved;
            bool created = Open(true);
            if (created)
            {
                F.Title = "Confirmation de temps - ordre " + OrderService.DocNum(orderEntry);
                FillOperations(opLineId);
            }
        }

        protected override void Build()
        {
            const int lw = 130, x = 10;
            int y = 10;
            AddUds("udOp", BoDataType.dt_SHORT_TEXT, 10);
            AddUds("udDate", BoDataType.dt_DATE);
            AddUds("udEmp", BoDataType.dt_SHORT_TEXT, 11);
            AddUds("udEmpNm", BoDataType.dt_SHORT_TEXT, 100);
            AddUds("udHrs", BoDataType.dt_QUANTITY);
            AddUds("udFinal", BoDataType.dt_SHORT_TEXT, 1);
            AddUds("udRem", BoDataType.dt_SHORT_TEXT, 200);
            AddUds("udInfo", BoDataType.dt_SHORT_TEXT, 200);

            U.Label("lOp", "Opération", x, y, lw, "cOp");
            U.ComboUds("cOp", x + lw, y, 360, "udOp");
            y += Ui.Step;
            U.ReadOnlyUds("eInfo", x + lw, y, 360, "udInfo");
            y += Ui.Step + 6;
            U.Label("lDate", "Date", x, y, lw, "eDate");
            U.EditUds("eDate", x + lw, y, 100, "udDate");
            SetUds("udDate", DateValue(DateTime.Today));
            y += Ui.Step;
            U.Cfl("cflEmp", "171");
            U.Label("lEmp", "Salarié (option)", x, y, lw, "eEmp");
            Ui.BindCfl(U.EditUds("eEmp", x + lw, y, 80, "udEmp"), "cflEmp", "empID");
            U.ReadOnlyUds("eEmpNm", x + lw + 85, y, 275, "udEmpNm");
            y += Ui.Step;
            U.Label("lHrs", "Heures travaillées", x, y, lw, "eHrs");
            U.EditUds("eHrs", x + lw, y, 80, "udHrs");
            U.Label("lHrsI", "(négatif = correction)", x + lw + 85, y, 200);
            y += Ui.Step;
            U.CheckUds("cFinal", "Confirmation finale (opération terminée)", x + lw, y, 300, "udFinal");
            SetUds("udFinal", "N");
            y += Ui.Step;
            U.Label("lRem", "Commentaire", x, y, lw, "eRem");
            U.EditUds("eRem", x + lw, y, 360, "udRem");

            U.Button("bOk", "Valider", x, FormHeight - 62, 90);
            U.Button("bCancel", "Annuler", x + 95, FormHeight - 62, 90);
        }

        private void FillOperations(int opLineId)
        {
            ComboBox c = (ComboBox)F.Items.Item("cOp").Specific;
            _ops.Clear();
            string first = null, wanted = null;
            foreach (Row r in Sql.Rows("SELECT \"LineId\", \"U_OpNo\", \"U_Descr\", \"U_WorkCtr\", \"U_PlanHrs\", \"U_ActHrs\", \"U_Done\" FROM " +
                                       Db.T(Db.OrderOps) + " WHERE \"DocEntry\" = " + _order + " ORDER BY \"U_OpNo\""))
            {
                string id = r.Int("LineId").ToString(CultureInfo.InvariantCulture);
                _ops[id] = r;
                c.ValidValues.Add(id, r.Str("U_OpNo") + " - " + NotificationService.Truncate(r.Str("U_Descr"), 60) + " (" + r.Str("U_WorkCtr") + ")");
                if (first == null && r.Str("U_Done") != "Y")
                    first = id;
                if (r.Int("LineId") == opLineId)
                    wanted = id;
            }
            string selected = wanted ?? first ?? (_ops.Count > 0 ? c.ValidValues.Item(0).Value : "");
            SetUds("udOp", selected);
            ShowOpInfo();
        }

        private void ShowOpInfo()
        {
            if (!_ops.TryGetValue(Uds("udOp"), out Row r))
            {
                SetUds("udInfo", "");
                return;
            }
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            SetUds("udInfo", "Prévu " + r.Dbl("U_PlanHrs").ToString("N2", fr) + " h, déjà confirmé " + r.Dbl("U_ActHrs").ToString("N2", fr) + " h" +
                             (r.Str("U_Done") == "Y" ? " - terminée" : ""));
        }

        protected override void OnEvent(ItemEvent e)
        {
            if (e.EventType == BoEventTypes.et_COMBO_SELECT && e.ItemUID == "cOp")
            {
                ShowOpInfo();
                return;
            }
            if (e.EventType == BoEventTypes.et_CHOOSE_FROM_LIST && e.ItemUID == "eEmp")
            {
                IChooseFromListEvent cfe = (IChooseFromListEvent)e;
                DataTable sel = cfe.SelectedObjects;
                if (sel == null || sel.Rows.Count == 0)
                    return;
                SetUds("udEmp", Convert.ToString(sel.GetValue("empID", 0), CultureInfo.InvariantCulture));
                SetUds("udEmpNm", (Convert.ToString(sel.GetValue("firstName", 0)) + " " + Convert.ToString(sel.GetValue("lastName", 0))).Trim());
                return;
            }
            if (e.EventType != BoEventTypes.et_ITEM_PRESSED || !e.ActionSuccess)
                return;
            if (e.ItemUID == "bCancel")
                Close();
            else if (e.ItemUID == "bOk")
                Save();
        }

        private void Save()
        {
            if (!int.TryParse(Uds("udOp"), out int lineId))
            {
                Msg("Choisissez l'opération.", BoStatusBarMessageType.smt_Warning);
                return;
            }
            DateTime? date = Sql.ParseDate(Uds("udDate"));
            if (date == null)
            {
                Msg("Saisissez la date.", BoStatusBarMessageType.smt_Warning);
                return;
            }
            int.TryParse(Uds("udEmp"), out int emp);
            var draft = new ConfirmationDraft
            {
                OpLineId = lineId,
                Date = date.Value,
                EmpId = emp,
                Hours = Sql.ParseDouble(Uds("udHrs")),
                Final = Uds("udFinal") == "Y",
                Remarks = Uds("udRem")
            };
            OrderService.Confirm(_order, draft);
            Close();
            Msg("Confirmation enregistrée.");
            try
            {
                _onSaved?.Invoke();
            }
            catch (Exception ex)
            {
                App.MessageBox("La confirmation est enregistrée, mais l'ordre n'a pas pu être actualisé : " + ex.Message);
            }
        }
    }
}
