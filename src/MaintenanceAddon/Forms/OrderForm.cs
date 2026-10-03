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
    /// Ordre de maintenance (IW31 / IW32 / IW33) : en-tête, opérations,
    /// composants, coûts prévus / réels, confirmations, documents liés ;
    /// statuts CRTD → REL → TECO → CLSD.
    /// </summary>
    internal sealed class OrderForm : UdoForm
    {
        private const string DtConf = "dtConf";
        private const string DtDocs = "dtDocs";

        private static readonly string[] Closed = { OrderStatus.TechCompleted, OrderStatus.Closed, OrderStatus.Cancelled };

        public OrderForm(Application app) : base(app) { }

        public override string ObjectCode => Obj.Order;
        protected override string FormType => FormIds.OrderForm;
        protected override string HeaderTable => Db.Order;
        protected override string[] ChildTables => new[] { Db.OrderOps, Db.OrderComps };
        protected override bool IsDocument => true;
        protected override string Title => "Ordre de maintenance";
        protected override int FormWidth => 960;
        protected override int FormHeight => 700;

        protected override void Build()
        {
            const int lw = 100, x = 10, x2 = 330, x3 = 650;
            int y = 10;
            BuildKeyFields(x, y, lw);
            U.Label("lType", "Type d'ordre", x2, y, lw, "cType");
            U.Combo("cType", x2 + lw, y, 180, "U_OrdType", OrderTypes.List);
            U.Label("lStatus", "Statut", x3, y, lw, "cStatus");
            U.Combo("cStatus", x3 + lw, y, 180, "U_Status", OrderStatus.List);
            U.Editable("cStatus", false, true, false);
            y += Ui.Step;
            U.Label("lPrio", "Priorité", x2, y, lw, "cPrio");
            U.Combo("cPrio", x2 + lw, y, 180, "U_Priority", Priorities.List);
            U.Field("lNotif", "Avis", "eNotif", x3, y, lw, 80, "U_NotifNo");
            U.Editable("eNotif", false, true, false);
            RegisterUdoLink("kNotif", "eNotif", Obj.Notif);
            y += Ui.Step + 6;

            U.Cfl("cflEq", Obj.Equip);
            Ui.BindCfl(U.Field("lEq", "Équipement", "eEq", x, y, lw, 110, "U_Equip"), "cflEq", "Code");
            RegisterCfl("eEq", null, "@" + Db.Order, "U_Equip", "Code");
            RegisterUdoLink("kEq", "eEq", Obj.Equip);
            AddName("udEqNm", "eEqNm", x + lw + 115, y, 200);
            U.Field("lStart", "Début prévu", "eStart", x3, y, lw, 90, "U_StartDt");
            y += Ui.Step;

            U.Cfl("cflFl", Obj.FuncLoc);
            Ui.BindCfl(U.Field("lFl", "Poste technique", "eFl", x, y, lw, 110, "U_FuncLoc"), "cflFl", "Code");
            RegisterCfl("eFl", null, "@" + Db.Order, "U_FuncLoc", "Code");
            RegisterUdoLink("kFl", "eFl", Obj.FuncLoc);
            AddName("udFlNm", "eFlNm", x + lw + 115, y, 200);
            U.Field("lEnd", "Fin prévue", "eEnd", x3, y, lw, 90, "U_EndDt");
            y += Ui.Step;

            U.Cfl("cflWc", Obj.WorkCtr);
            Ui.BindCfl(U.Field("lWc", "Poste de travail", "eWc", x, y, lw, 110, "U_WorkCtr"), "cflWc", "Code");
            RegisterCfl("eWc", null, "@" + Db.Order, "U_WorkCtr", "Code");
            U.Cfl("cflUsr", "12");
            Ui.BindCfl(U.Field("lResp", "Responsable", "eResp", x2, y, lw, 110, "U_Respons"), "cflUsr", "USER_CODE");
            RegisterCfl("eResp", null, "@" + Db.Order, "U_Respons", "USER_CODE");
            U.Field("lAStart", "Début réel", "eAStart", x3, y, lw, 90, "U_ActStart");
            U.Editable("eAStart", false, true, false);
            y += Ui.Step;

            ChooseFromList cflOcr = U.Cfl("cflOcr", "61");
            Ui.CflFilter(cflOcr, "DimCode", SettingsService.Load().Dimension.ToString(CultureInfo.InvariantCulture));
            Ui.BindCfl(U.Field("lOcr", "Centre de coûts", "eOcr", x, y, lw, 110, "U_OcrCode"), "cflOcr", "PrcCode");
            RegisterCfl("eOcr", null, "@" + Db.Order, "U_OcrCode", "PrcCode");
            U.Cfl("cflTsk", Obj.TaskList);
            Ui.BindCfl(U.Field("lTsk", "Reprendre la gamme", "eTsk", x2, y, lw, 110, "U_TaskList"), "cflTsk", "Code");
            RegisterCfl("eTsk", null, "@" + Db.Order, "U_TaskList", "Code");
            RegisterUdoLink("kTsk", "eTsk", Obj.TaskList);
            U.Field("lAEnd", "Fin réelle", "eAEnd", x3, y, lw, 90, "U_ActEnd");
            U.Editable("eAEnd", false, true, false);
            y += Ui.Step;

            U.Field("lPlan", "Plan d'entretien", "ePlan", x, y, lw, 110, "U_PlanCode");
            U.Editable("ePlan", false, true, false);
            RegisterUdoLink("kPlan", "ePlan", Obj.Plan);
            U.Field("lDue", "Échéance du plan", "eDue", x2, y, lw, 90, "U_CallDue");
            U.Editable("eDue", false, true, false);
            U.Field("lTeco", "Clôt. technique", "eTeco", x3, y, lw, 90, "U_TecoDate");
            U.Editable("eTeco", false, true, false);
            y += Ui.Step;

            U.Field("lCtr", "Contrat", "eCtr", x, y, lw, 110, "U_Contract");
            U.Editable("eCtr", false, true, false);
            RegisterUdoLink("kCtr", "eCtr", Obj.Contract);
            U.Check("cWar", "Sous garantie", x2, y, 120, "U_UnderWar");
            U.Editable("cWar", false, true, false);
            U.Check("cCap", "À immobiliser", x3, y, 100, "U_Capital");
            ChooseFromList cflCap = U.Cfl("cflCap", "1");
            Ui.CflFilter(cflCap, "Postable", "Y");
            Ui.BindCfl(U.Edit("eCap", x3 + lw, y, 90, "U_CapAcct"), "cflCap", "AcctCode");
            RegisterCfl("eCap", null, "@" + Db.Order, "U_CapAcct", "AcctCode");
            y += Ui.Step + 4;

            U.Field("lSubj", "Description courte", "eSubj", x, y, lw, FormWidth - lw - 40, "U_Subject");
            y += Ui.Step;
            F.DataSources.UserDataSources.Add("udCtx", BoDataType.dt_LONG_TEXT, 254);
            U.ReadOnlyUds("eCtx", x + lw, y, FormWidth - lw - 40, "udCtx");
            y += Ui.Step + 6;

            int ft = y;
            AddFolder("fOps", "Opérations", x, ft, 100, 1);
            AddFolder("fComps", "Composants", x + 100, ft, 100, 2);
            AddFolder("fCosts", "Coûts", x + 200, ft, 90, 3);
            AddFolder("fConf", "Confirmations", x + 290, ft, 100, 4);
            AddFolder("fDocs", "Documents liés", x + 390, ft, 110, 5);
            AddFolder("fDescr", "Description", x + 500, ft, 100, 6);
            int top = ft + 25;
            U.Frame("rFrame", x, ft + 19, FormWidth - 30, FormHeight - ft - 110);
            int mh = FormHeight - top - 125, by = FormHeight - 120;

            // ---- Opérations --------------------------------------------------
            U.Pane = 1;
            OpCompMatrices.BuildOperations(U, "mOps", Db.OrderOps, x + 10, top, FormWidth - 50, mh, true);
            U.Button("bOpAdd", "Ajouter une ligne", x + 10, by, 120);
            U.Button("bOpDel", "Supprimer la ligne", x + 135, by, 120);
            U.Button("bConf", "Confirmer du temps...", x + 260, by, 140);
            U.Button("bShip", "Envoi / retour prestataire...", x + 405, by, 170);
            RegisterMatrix("mOps", Db.OrderOps, "U_Descr", "bOpAdd", "bOpDel", (ds, row) =>
            {
                OpCompMatrices.NewOperation(ds, row, H("U_WorkCtr"));
                ds.SetValue("U_Done", row, "N");
            });
            RegisterCfl("mOps", "cWc", "@" + Db.OrderOps, "U_WorkCtr", "Code");
            RegisterCfl("mOps", "cVend", "@" + Db.OrderOps, "U_Vendor", "CardCode");

            // ---- Composants --------------------------------------------------
            U.Pane = 2;
            OpCompMatrices.BuildComponents(U, "mComps", Db.OrderComps, x + 10, top, FormWidth - 50, mh, true);
            U.Button("bCpAdd", "Ajouter une ligne", x + 10, by, 120);
            U.Button("bCpDel", "Supprimer la ligne", x + 135, by, 120);
            U.Button("bIssue", "Sortie de stock...", x + 260, by, 130);
            U.Button("bReturn", "Retour en stock...", x + 395, by, 130);
            RegisterMatrix("mComps", Db.OrderComps, "U_ItemCode", "bCpAdd", "bCpDel", (ds, row) => OpCompMatrices.NewComponent(ds, row, DefaultWarehouse()));
            RegisterCfl("mComps", "cItem", "@" + Db.OrderComps, "U_ItemCode", "ItemCode");
            RegisterCfl("mComps", "cWhs", "@" + Db.OrderComps, "U_Whs", "WhsCode");

            // ---- Coûts -------------------------------------------------------
            U.Pane = 3;
            int cx = x + 30, cy = top + 10;
            U.Label("lcPl", "Prévu", cx + 160, cy, 120);
            U.Label("lcAc", "Réel", cx + 290, cy, 120);
            U.Label("lcDf", "Écart", cx + 420, cy, 120);
            cy += Ui.Step + 2;
            CostRow("Lab", "Main-d'oeuvre", cx, cy, "U_PlLab", "U_AcLab");
            CostRow("Mat", "Pièces (sorties − retours)", cx, cy + Ui.Step, "U_PlMat", "U_AcMat");
            CostRow("Ext", "Prestations externes", cx, cy + 2 * Ui.Step, "U_PlExt", "U_AcExt");
            F.DataSources.UserDataSources.Add("udPlTot", BoDataType.dt_SUM);
            F.DataSources.UserDataSources.Add("udAcTot", BoDataType.dt_SUM);
            F.DataSources.UserDataSources.Add("udDfTot", BoDataType.dt_SUM);
            int ty = cy + 3 * Ui.Step + 4;
            U.Label("lcTot", "Total", cx, ty, 150);
            U.ReadOnlyUds("ePlTot", cx + 160, ty, 120, "udPlTot");
            U.ReadOnlyUds("eAcTot", cx + 290, ty, 120, "udAcTot");
            U.ReadOnlyUds("eDfTot", cx + 420, ty, 120, "udDfTot");
            U.Label("lcInfo", "Prévu : heures × taux du poste, quantités × coût moyen, prestations prévues.", cx, ty + 30, 600);
            U.Label("lcInfo2", "Réel : confirmations, sorties de stock nettes des retours, factures fournisseurs imputées (hors articles stockés).", cx, ty + 30 + Ui.Step, 700);
            U.Button("bRecalc", "Recalculer les coûts", cx, ty + 30 + 2 * Ui.Step + 10, 150);
            F.DataSources.UserDataSources.Add("udSett", BoDataType.dt_LONG_TEXT, 254);
            U.ReadOnlyUds("eSett", cx, ty + 30 + 4 * Ui.Step, 700, "udSett");

            // ---- Confirmations / documents -----------------------------------
            U.Pane = 4;
            U.Grid("gConf", DtConf, x + 10, top, FormWidth - 50, mh + 25);
            U.Pane = 5;
            U.Grid("gDocs", DtDocs, x + 10, top, FormWidth - 50, mh + 25);
            U.Pane = 6;
            U.Memo("eDescr", x + 10, top, FormWidth - 50, mh + 25, "U_Descr");
            U.Pane = 0;

            int bx = 150, bw = 112, bt = FormHeight - 62;
            U.Button("bRel", "Lancer", bx, bt, bw);
            U.Button("bTeco", "Clôture technique", bx + (bw + 4), bt, bw);
            U.Button("bUndo", "Annuler la TECO", bx + 2 * (bw + 4), bt, bw);
            U.Button("bClose", "Clôturer", bx + 3 * (bw + 4), bt, bw);
            U.Button("bCancel", "Annuler l'ordre", bx + 4 * (bw + 4), bt, bw);
            U.Button("bPR", "Demandes d'achat", bx + 5 * (bw + 4), bt, bw);
        }

        private void AddName(string uds, string id, int left, int top, int width)
        {
            F.DataSources.UserDataSources.Add(uds, BoDataType.dt_SHORT_TEXT, 100);
            U.ReadOnlyUds(id, left, top, width, uds);
        }

        private void CostRow(string suffix, string caption, int left, int top, string planned, string actual)
        {
            U.Label("lc" + suffix, caption, left, top, 155);
            U.Edit("eP" + suffix, left + 160, top, 120, planned);
            U.Editable("eP" + suffix, false, false, false);
            U.Edit("eA" + suffix, left + 290, top, 120, actual);
            U.Editable("eA" + suffix, false, false, false);
            F.DataSources.UserDataSources.Add("udD" + suffix, BoDataType.dt_SUM);
            U.ReadOnlyUds("eD" + suffix, left + 420, top, 120, "udD" + suffix);
        }

        private string DefaultWarehouse()
        {
            EquipmentInfo eq = EquipmentInfo.Load(H("U_Equip"));
            return eq != null && eq.Whs != "" ? eq.Whs : SettingsService.Load().DefaultWarehouse;
        }

        protected override void SetDefaults()
        {
            SetH("U_OrdType", OrderTypes.Corrective);
            SetH("U_Status", OrderStatus.Created);
            SetH("U_Priority", "3");
            SetH("U_Respons", DiCompany.UserCode);
            SetH("U_StartDt", DateTime.Today);
            SetH("U_UnderWar", "N");
            SetH("U_Capital", "N");
            ApplyPriority();
        }

        private ServiceContext Context()
        {
            return H("U_Equip") == "" ? null : ServiceContext.For(H("U_Equip"), HDate("U_StartDt") ?? DateTime.Today, H("U_OrdType"));
        }

        private void ApplyPriority()
        {
            DateTime start = HDate("U_StartDt") ?? DateTime.Today;
            SetH("U_EndDt", start.Date.Add(DateTime.Now.TimeOfDay).AddHours(SettingsService.Load().HoursFor(H("U_Priority"))).Date);
        }

        protected override string Validate()
        {
            if (Array.IndexOf(Closed, H("U_Status")) >= 0 && F.Mode == BoFormMode.fm_UPDATE_MODE)
                return "L'ordre est " + OrderStatus.List.Caption(H("U_Status")).ToLowerInvariant() + " : il n'est plus modifiable.";
            if (H("U_Equip") == "" && H("U_FuncLoc") == "")
                return "Indiquez l'équipement ou le poste technique de l'ordre.";
            if (H("U_Equip") != "" && EquipmentInfo.Load(H("U_Equip")) == null)
                return "Équipement inconnu : " + H("U_Equip");
            if (H("U_Subject") == "")
                return "Saisissez la description courte de l'ordre.";
            DateTime? s = HDate("U_StartDt"), e = HDate("U_EndDt");
            if (s == null || e == null)
                return "Renseignez les dates de début et de fin prévues.";
            if (e < s)
                return "La fin prévue précède le début prévu.";
            if (H("U_Capital") == "Y" && H("U_CapAcct") == "")
                SetH("U_CapAcct", SettingsService.Load().CapitalAccount);
            if (H("U_CapAcct") != "")
            {
                try { SettingsService.CheckAccount(H("U_CapAcct"), "Compte de règlement"); }
                catch (InvalidOperationException ex) { return ex.Message; }
            }
            if (F.Mode == BoFormMode.fm_ADD_MODE)
            {
                // Garantie / contrat de l'équipement pour ce type d'ordre, à la date de début
                ServiceContext ctx = Context();
                SetH("U_UnderWar", ctx != null && ctx.UnderWarranty ? "Y" : "N");
                SetH("U_Contract", ctx?.Contract?.Code ?? "");
            }
            return OpCompMatrices.Validate(Lines(Db.OrderOps), Lines(Db.OrderComps));
        }

        protected override string CanDeleteLine(string table, DBDataSource ds, int row)
        {
            int lineId;
            int.TryParse(ds.GetValue("LineId", row).Trim(), out lineId);
            int order = CurrentDocEntry;
            if (Sql.ParseDouble(ds.GetValue("U_PrEntry", row)) > 0)
                return "Une demande d'achat existe pour cette ligne : elle ne peut pas être supprimée.";
            if (order == 0)
                return null;
            if (table == Db.OrderOps && Sql.Exists("SELECT 1 FROM " + Db.T(Db.Conf) + " WHERE \"U_OrderNo\" = " + order + " AND \"U_OpLine\" = " + lineId))
                return "Des temps ont été confirmés sur cette opération : elle ne peut pas être supprimée.";
            if (table == Db.OrderComps && Sql.Exists("SELECT 1 FROM \"IGE1\" WHERE \"" + DocFields.Order + "\" = " + order + " AND \"" + DocFields.Line + "\" = " + lineId))
                return "Des pièces ont été sorties sur cette ligne : elle ne peut pas être supprimée.";
            return null;
        }

        protected override void AfterSaved(string key, bool added)
        {
            int.TryParse(key, out int entry);
            if (entry <= 0)
                return;
            OrderService.RecalcCosts(entry);
            if (!added)
                Reload();
        }

        protected override void Refresh()
        {
            SetUds("udEqNm", NotificationService.NameOf(Db.Equip, H("U_Equip")));
            SetUds("udFlNm", NotificationService.NameOf(Db.FuncLoc, H("U_FuncLoc")));
            ServiceContext ctx = Context();
            SetUds("udCtx", ctx?.Banner() ?? "");
            int settJe = (int)HDbl("U_SettJE");
            SetUds("udSett", settJe > 0
                ? "Ordre réglé sur le compte " + H("U_CapAcct") + " : écriture n° " + settJe + ", montant " + Sql.Amount(HDbl("U_SettAmt")) + "."
                : H("U_Capital") == "Y" ? "Ordre à immobiliser : à la clôture, ses coûts comptabilisés seront transférés sur le compte " +
                                          (H("U_CapAcct") == "" ? "d'immobilisations en cours des paramètres" : H("U_CapAcct")) + "." : "");

            double pl = 0, ac = 0;
            foreach (var c in new[] { new[] { "Lab", "U_PlLab", "U_AcLab" }, new[] { "Mat", "U_PlMat", "U_AcMat" }, new[] { "Ext", "U_PlExt", "U_AcExt" } })
            {
                double p = HDbl(c[1]), a = HDbl(c[2]);
                pl += p;
                ac += a;
                SetUds("udD" + c[0], Sql.N(a - p));
            }
            SetUds("udPlTot", Sql.N(pl));
            SetUds("udAcTot", Sql.N(ac));
            SetUds("udDfTot", Sql.N(ac - pl));

            int entry = CurrentDocEntry;
            U.LoadGrid("gConf", DtConf, ReportService.OrderConfirmationsSql(entry));
            U.LoadGrid("gDocs", DtDocs, ReportService.OrderDocumentsSql(entry));

            string st = entry == 0 ? "" : H("U_Status");
            bool editable = entry == 0 || st == OrderStatus.Created || st == OrderStatus.Released;
            foreach (string id in new[] { "mOps", "mComps", "bOpAdd", "bOpDel", "bCpAdd", "bCpDel" })
                Enable(id, editable);
            Enable("bRel", st == OrderStatus.Created);
            Enable("bTeco", st == OrderStatus.Released);
            Enable("bUndo", st == OrderStatus.TechCompleted);
            Enable("bClose", st == OrderStatus.TechCompleted);
            Enable("bCancel", st == OrderStatus.Created || st == OrderStatus.Released);
            Enable("bPR", st == OrderStatus.Created || st == OrderStatus.Released);
            Enable("bConf", st == OrderStatus.Released);
            Enable("bIssue", st == OrderStatus.Released);
            Enable("bReturn", st == OrderStatus.Released);
            Enable("bRecalc", entry > 0 && st != OrderStatus.Cancelled && st != OrderStatus.Closed);
            Enable("bShip", (st == OrderStatus.Created || st == OrderStatus.Released) && H("U_Equip") != "");
            Enable("cCap", editable);
            Enable("eCap", editable);
        }

        protected override void OnComboSelect(string itemUid, string colUid, int row)
        {
            if (itemUid == "cPrio" && F.Mode == BoFormMode.fm_ADD_MODE)
                ApplyPriority();
            else if (itemUid == "cType" && F.Mode == BoFormMode.fm_ADD_MODE)
            {
                // Une amélioration (PM03) est en principe immobilisée
                SetH("U_Capital", H("U_OrdType") == OrderTypes.Improvement ? "Y" : "N");
                Refresh();
            }
            else if (itemUid == "mOps" && colUid == "cCtrl" && row > 0)
                DefaultExternalVendor(row);
        }

        /// <summary>Opération passée en externe : prestataire par défaut (garant, contrat, attitré).</summary>
        private void DefaultExternalVendor(int row)
        {
            Matrix m = Mat("mOps");
            m.FlushToDataSource();
            DBDataSource ds = Lines(Db.OrderOps);
            if (row > ds.Size || ds.GetValue("U_CtrlKey", row - 1).Trim() != ControlKeys.External)
                return;
            ServiceContext ctx = Context();
            if (ctx == null)
                return;
            if (ds.GetValue("U_Vendor", row - 1).Trim() == "")
                ds.SetValue("U_Vendor", row - 1, ctx.DefaultVendor);
            if (ctx.LaborCovered)
            {
                ds.SetValue("U_ExtCost", row - 1, "0");
                Msg("Prestation couverte (" + (ctx.UnderWarranty ? "garantie" : "contrat " + ctx.Contract.Code) + ") : coût prévu à 0.", BoStatusBarMessageType.smt_Warning);
            }
            m.LoadFromDataSource();
        }

        protected override void OnChosen(string itemUid, string colUid, int row, DataTable selected)
        {
            switch (itemUid)
            {
                case "eEq":
                    EquipmentInfo eq = EquipmentInfo.Load(H("U_Equip"));
                    if (eq != null)
                    {
                        SetH("U_FuncLoc", eq.FuncLoc);
                        if (H("U_WorkCtr") == "") SetH("U_WorkCtr", eq.WorkCtr);
                        if (H("U_OcrCode") == "") SetH("U_OcrCode", eq.OcrCode);
                    }
                    break;
                case "eTsk":
                    CopyTaskList(H("U_TaskList"));
                    break;
                case "mComps":
                    if (colUid == "cItem")
                    {
                        DBDataSource ds = Lines(Db.OrderComps);
                        OpCompMatrices.OnItemChosen(ds, row - 1, selected);
                        ds.SetValue("U_UnitCost", row - 1, Sql.N(OrderService.ItemCost(ds.GetValue("U_ItemCode", row - 1).Trim(), ds.GetValue("U_Whs", row - 1).Trim())));
                    }
                    break;
            }
        }

        /// <summary>Ajoute à l'ordre les opérations et composants de la gamme.</summary>
        private void CopyTaskList(string taskList)
        {
            if (string.IsNullOrEmpty(taskList))
                return;
            if (Array.IndexOf(Closed, H("U_Status")) >= 0)
                return;
            var ops = new List<OpDraft>();
            var comps = new List<CompDraft>();
            OrderService.LoadTaskList(taskList, ops, comps);
            if (ops.Count == 0 && comps.Count == 0)
                return;

            Matrix mOps = Mat("mOps"), mComps = Mat("mComps");
            mOps.FlushToDataSource();
            mComps.FlushToDataSource();
            DBDataSource dOps = Lines(Db.OrderOps), dComps = Lines(Db.OrderComps);
            if ((dOps.Size > 0 || dComps.Size > 0) &&
                !Confirm("Ajouter les " + ops.Count + " opération(s) et " + comps.Count + " composant(s) de la gamme " + taskList + " à l'ordre ?"))
                return;

            string mainWc = H("U_WorkCtr");
            string whs = DefaultWarehouse();
            int offset = MaxOpNo(dOps);
            foreach (OpDraft op in ops)
            {
                int row = NewRow(dOps);
                int n;
                int.TryParse(op.OpNo, out n);
                dOps.SetValue("U_OpNo", row, (offset + n).ToString("D4", CultureInfo.InvariantCulture));
                dOps.SetValue("U_Descr", row, op.Descr);
                dOps.SetValue("U_WorkCtr", row, op.WorkCtr == "" ? mainWc : op.WorkCtr);
                dOps.SetValue("U_CtrlKey", row, op.CtrlKey);
                dOps.SetValue("U_PlanHrs", row, Sql.N(op.PlanHrs));
                dOps.SetValue("U_NbPers", row, op.NbPers.ToString(CultureInfo.InvariantCulture));
                dOps.SetValue("U_ExtCost", row, Sql.N(op.ExtCost));
                dOps.SetValue("U_Vendor", row, op.Vendor);
                dOps.SetValue("U_Done", row, "N");
            }
            foreach (CompDraft c in comps)
            {
                int row = NewRow(dComps);
                int n;
                int.TryParse(c.OpNo, out n);
                dComps.SetValue("U_ItemCode", row, c.ItemCode);
                dComps.SetValue("U_ItemName", row, c.ItemName);
                dComps.SetValue("U_Qty", row, Sql.N(c.Qty));
                string compWhs = c.Whs == "" ? whs : c.Whs;
                dComps.SetValue("U_Whs", row, compWhs);
                dComps.SetValue("U_OpNo", row, c.OpNo == "" ? "" : (offset + n).ToString("D4", CultureInfo.InvariantCulture));
                dComps.SetValue("U_Proc", row, c.Proc);
                dComps.SetValue("U_UnitCost", row, Sql.N(OrderService.ItemCost(c.ItemCode, compWhs)));
            }
            mOps.LoadFromDataSource();
            mComps.LoadFromDataSource();
            if (H("U_OrdType") == "")
                SetH("U_OrdType", Sql.ScalarStr("SELECT \"U_OrdType\" FROM " + Db.T(Db.TaskList) + " WHERE \"Code\" = " + Sql.Q(taskList)));
            Msg("Gamme " + taskList + " reprise : vérifiez les opérations et composants avant d'enregistrer.");
        }

        private static int MaxOpNo(DBDataSource ds)
        {
            int max = 0;
            for (int i = 0; i < ds.Size; i++)
                if (int.TryParse(ds.GetValue("U_OpNo", i).Trim(), out int n))
                    max = Math.Max(max, n);
            return max;
        }

        private static int NewRow(DBDataSource ds)
        {
            int max = 0;
            for (int i = 0; i < ds.Size; i++)
                if (int.TryParse(ds.GetValue("LineId", i).Trim(), out int id))
                    max = Math.Max(max, id);
            ds.InsertRecord(ds.Size);
            int row = ds.Size - 1;
            ds.SetValue("LineId", row, (max + 1).ToString(CultureInfo.InvariantCulture));
            return row;
        }

        protected override void OnButton(string itemUid)
        {
            string[] actions = { "bRel", "bTeco", "bUndo", "bClose", "bCancel", "bPR", "bConf", "bIssue", "bReturn", "bRecalc", "bShip" };
            if (Array.IndexOf(actions, itemUid) < 0 || !RequireSaved())
                return;
            int entry = CurrentDocEntry;
            string num = H("DocNum");

            switch (itemUid)
            {
                case "bRel":
                    OrderService.Release(entry);
                    Reload();
                    Msg("Ordre " + num + " lancé.");
                    break;

                case "bTeco":
                    {
                        string warn = OpenWork(entry);
                        if (!Confirm((warn == "" ? "" : warn + "\n\n") + "Clôturer techniquement l'ordre " + num + " à la date du jour ?\n" +
                                     "L'avis lié sera terminé et le plan de maintenance mis à jour."))
                            return;
                        OrderService.TechnicallyComplete(entry, DateTime.Today);
                        Reload();
                        Msg("Ordre " + num + " clôturé techniquement.");
                        break;
                    }

                case "bUndo":
                    if (!Confirm("Annuler la clôture technique de l'ordre " + num + " ?\nLes dates du plan de maintenance éventuel ne sont pas recalculées."))
                        return;
                    OrderService.UndoTechnicalCompletion(entry);
                    Reload();
                    Msg("Clôture technique annulée.");
                    break;

                case "bClose":
                    if (!Confirm("Clôturer définitivement l'ordre " + num + " ? Plus aucune imputation ne sera possible." +
                                 (H("U_Capital") == "Y" ? "\nSes coûts comptabilisés seront transférés en immobilisation (écriture de règlement)." : "")))
                        return;
                    OrderService.Close(entry);
                    Reload();
                    Msg("Ordre " + num + " clôturé.");
                    break;

                case "bCancel":
                    if (!Confirm("Annuler l'ordre " + num + " ?"))
                        return;
                    OrderService.Cancel(entry);
                    Reload();
                    Msg("Ordre " + num + " annulé.");
                    break;

                case "bPR":
                    {
                        string result = OrderService.CreatePurchaseRequests(entry);
                        Reload();
                        App.MessageBox(result);
                        break;
                    }

                case "bConf":
                    {
                        // Opération proposée : la dernière ligne cliquée dans la matrice
                        int row = LastClickedRow("mOps");
                        int lineId = 0;
                        DBDataSource ops = Lines(Db.OrderOps);
                        if (row > 0 && row <= ops.Size)
                            int.TryParse(ops.GetValue("LineId", row - 1).Trim(), out lineId);
                        Navigator.Confirmation.Show(entry, lineId, Reload);
                        break;
                    }

                case "bIssue":
                    Navigator.Goods.Show(entry, true, Reload);
                    break;

                case "bReturn":
                    Navigator.Goods.Show(entry, false, Reload);
                    break;

                case "bShip":
                    Navigator.Shipment.Show(H("U_Equip"), entry, Reload);
                    break;

                case "bRecalc":
                    OrderService.RecalcCosts(entry);
                    Reload();
                    Msg("Coûts recalculés.");
                    break;
            }
        }

        /// <summary>Travaux non soldés (opérations non terminées, pièces non sorties), pour avertir avant la TECO.</summary>
        private string OpenWork(int entry)
        {
            var notes = new List<string>();
            int ops = (int)Sql.ScalarDbl("SELECT COUNT(*) FROM " + Db.T(Db.OrderOps) + " WHERE \"DocEntry\" = " + entry +
                                         " AND ISNULL(\"U_Done\", 'N') <> 'Y' AND ISNULL(\"U_CtrlKey\", 'INT') = 'INT'");
            if (ops > 0)
                notes.Add(ops + " opération(s) interne(s) sans confirmation finale");
            int comps = (int)Sql.ScalarDbl("SELECT COUNT(*) FROM " + Db.T(Db.OrderComps) + " WHERE \"DocEntry\" = " + entry +
                                           " AND \"U_Proc\" = 'S' AND ISNULL(\"U_IssQty\", 0) < \"U_Qty\"");
            if (comps > 0)
                notes.Add(comps + " composant(s) stocké(s) pas entièrement sorti(s)");
            return notes.Count == 0 ? "" : "Attention : " + string.Join(", ", notes) + ".";
        }
    }
}
