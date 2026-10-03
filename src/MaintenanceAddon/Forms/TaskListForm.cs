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
    /// Matrices « Opérations » et « Composants », communes à la gamme et à l'ordre.
    /// </summary>
    internal static class OpCompMatrices
    {
        public static void BuildOperations(Ui u, string matrixId, string table, int left, int top, int width, int height, bool onOrder)
        {
            string t = "@" + table;
            Matrix m = u.Matrix(matrixId, left, top, width, height);
            u.Col(m, "#", "#", 25, t, "LineId", false);
            u.Col(m, "cOpNo", "Opération", 60, t, "U_OpNo", true);
            u.Col(m, "cDescr", "Description", onOrder ? 200 : 240, t, "U_Descr", true);
            u.Cfl("cflOpWc", Obj.WorkCtr);
            Ui.BindCfl(u.Col(m, "cWc", "Poste de travail", 85, t, "U_WorkCtr", true), "cflOpWc", "Code");
            Column ck = u.Col(m, "cCtrl", "Clé", 70, t, "U_CtrlKey", true, BoFormItemTypes.it_COMBO_BOX);
            foreach (var v in ControlKeys.List.Items)
                ck.ValidValues.Add(v.Key, v.Value);
            ck.DisplayDesc = true;
            u.Col(m, "cHrs", "Travail prévu (h)", 80, t, "U_PlanHrs", true);
            u.Col(m, "cPers", "Pers.", 40, t, "U_NbPers", true);
            u.Col(m, "cExt", "Coût presta.", 80, t, "U_ExtCost", true);
            ChooseFromList cflV = u.Cfl("cflOpVd", "2");
            Ui.CflFilter(cflV, "CardType", "S");
            Ui.BindCfl(u.Col(m, "cVend", "Prestataire", 80, t, "U_Vendor", true), "cflOpVd", "CardCode");
            if (onOrder)
            {
                u.Col(m, "cAct", "Réalisé (h)", 70, t, "U_ActHrs", false);
                Column done = u.Col(m, "cDone", "Terminée", 55, t, "U_Done", false, BoFormItemTypes.it_CHECK_BOX);
                done.ValOn = "Y";
                done.ValOff = "N";
                u.Col(m, "cPr", "DA", 45, t, "U_PrEntry", false);
            }
        }

        public static void BuildComponents(Ui u, string matrixId, string table, int left, int top, int width, int height, bool onOrder)
        {
            string t = "@" + table;
            Matrix m = u.Matrix(matrixId, left, top, width, height);
            u.Col(m, "#", "#", 25, t, "LineId", false);
            u.Cfl("cflItem", "4");
            Column item = u.Col(m, "cItem", "Article", 100, t, "U_ItemCode", true, BoFormItemTypes.it_LINKED_BUTTON);
            ((LinkedButton)item.ExtendedObject).LinkedObject = BoLinkedObject.lf_Items;
            Ui.BindCfl(item, "cflItem", "ItemCode");
            u.Col(m, "cName", "Désignation", 200, t, "U_ItemName", true);
            u.Col(m, "cQty", "Qté prévue", 70, t, "U_Qty", true);
            u.Cfl("cflCWhs", "64");
            Ui.BindCfl(u.Col(m, "cWhs", "Magasin", 65, t, "U_Whs", true), "cflCWhs", "WhsCode");
            u.Col(m, "cOpNo", "Opération", 60, t, "U_OpNo", true);
            Column pr = u.Col(m, "cProc", "Approvisionnement", 90, t, "U_Proc", true, BoFormItemTypes.it_COMBO_BOX);
            foreach (var v in Procurement.List.Items)
                pr.ValidValues.Add(v.Key, v.Value);
            pr.DisplayDesc = true;
            if (onOrder)
            {
                u.Col(m, "cCost", "Coût unit.", 75, t, "U_UnitCost", false);
                u.Col(m, "cIss", "Qté sortie", 70, t, "U_IssQty", false);
                u.Col(m, "cPr", "DA", 45, t, "U_PrEntry", false);
            }
        }

        /// <summary>Nouvelle opération : numéro suivant (pas de 10), clé interne, 1 personne.</summary>
        public static void NewOperation(DBDataSource ds, int row, string workCtr)
        {
            int max = 0;
            for (int i = 0; i < ds.Size; i++)
                if (i != row && int.TryParse(ds.GetValue("U_OpNo", i).Trim(), out int n))
                    max = Math.Max(max, n);
            ds.SetValue("U_OpNo", row, ((max / 10 + 1) * 10).ToString("D4", CultureInfo.InvariantCulture));
            ds.SetValue("U_CtrlKey", row, ControlKeys.Internal);
            ds.SetValue("U_NbPers", row, "1");
            ds.SetValue("U_WorkCtr", row, workCtr ?? "");
        }

        public static void NewComponent(DBDataSource ds, int row, string whs)
        {
            ds.SetValue("U_Proc", row, Procurement.Stock);
            ds.SetValue("U_Qty", row, "1");
            ds.SetValue("U_Whs", row, whs ?? "");
        }

        /// <summary>Choix d'article : désignation et magasin repris.</summary>
        public static void OnItemChosen(DBDataSource ds, int row, DataTable selected)
        {
            ds.SetValue("U_ItemName", row, Convert.ToString(selected.GetValue("ItemName", 0), CultureInfo.InvariantCulture));
            if (Convert.ToString(selected.GetValue("InvntItem", 0), CultureInfo.InvariantCulture) != "Y")
                ds.SetValue("U_Proc", row, Procurement.Purchase);
        }

        public static string Validate(DBDataSource ops, DBDataSource comps)
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < ops.Size; i++)
            {
                string op = ops.GetValue("U_OpNo", i).Trim();
                if (!seen.Add(op))
                    return "Le numéro d'opération " + op + " est en double.";
                if (ops.GetValue("U_Descr", i).Trim() == "")
                    return "Opération " + op + " : saisissez une description.";
                if (Sql.ParseDouble(ops.GetValue("U_PlanHrs", i)) < 0 || Sql.ParseDouble(ops.GetValue("U_ExtCost", i)) < 0)
                    return "Opération " + op + " : valeurs négatives interdites.";
                string wc = ops.GetValue("U_WorkCtr", i).Trim();
                if (wc != "" && !Sql.Exists("SELECT 1 FROM " + Db.T(Db.WorkCtr) + " WHERE \"Code\" = " + Sql.Q(wc)))
                    return "Opération " + op + " : poste de travail inconnu (" + wc + ").";
            }
            for (int i = 0; i < comps.Size; i++)
            {
                string item = comps.GetValue("U_ItemCode", i).Trim();
                if (!Sql.Exists("SELECT 1 FROM \"OITM\" WHERE \"ItemCode\" = " + Sql.Q(item)))
                    return "Article inconnu : " + item;
                if (Sql.ParseDouble(comps.GetValue("U_Qty", i)) <= 0)
                    return "Article " + item + " : la quantité prévue doit être positive.";
                string op = comps.GetValue("U_OpNo", i).Trim();
                if (op != "" && !seen.Contains(op))
                    return "Article " + item + " : l'opération " + op + " n'existe pas.";
            }
            return null;
        }
    }

    /// <summary>Gamme de maintenance (IA05 / IA06) : opérations et pièces types réutilisées par les ordres et les plans.</summary>
    internal sealed class TaskListForm : UdoForm
    {
        public TaskListForm(Application app) : base(app) { }

        public override string ObjectCode => Obj.TaskList;
        protected override string FormType => FormIds.TaskListForm;
        protected override string HeaderTable => Db.TaskList;
        protected override string[] ChildTables => new[] { Db.TaskOps, Db.TaskComps };
        protected override bool IsDocument => false;
        protected override string Title => "Gamme de maintenance";
        protected override int FormWidth => 900;
        protected override int FormHeight => 560;

        protected override void Build()
        {
            const int lw = 120, x = 10, x2 = 470;
            BuildKeyFields(x, 10, lw);

            U.Label("lCat", "Catégorie d'équipement", x2, 10, lw, "cCat");
            ComboBox cat = U.Combo("cCat", x2 + lw, 10, 200, "U_Category", null, true);
            foreach (Row r in Sql.Rows("SELECT \"Code\", \"Name\" FROM " + Db.T(Db.EqCat) + " ORDER BY \"Code\""))
                cat.ValidValues.Add(r.Str("Code"), r.Str("Name"));

            U.Cfl("cflEq", Obj.Equip);
            Ui.BindCfl(U.Field("lEq", "Équipement (option)", "eEq", x2, 10 + Ui.Step, lw, 120, "U_Equip"), "cflEq", "Code");
            RegisterCfl("eEq", null, "@" + Db.TaskList, "U_Equip", "Code");
            RegisterUdoLink("kEq", "eEq", Obj.Equip);

            U.Cfl("cflWc", Obj.WorkCtr);
            Ui.BindCfl(U.Field("lWc", "Poste de travail", "eWc", x, 10 + 2 * Ui.Step, lw, 120, "U_WorkCtr"), "cflWc", "Code");
            RegisterCfl("eWc", null, "@" + Db.TaskList, "U_WorkCtr", "Code");

            U.Label("lType", "Type d'ordre", x2, 10 + 2 * Ui.Step, lw, "cType");
            U.Combo("cType", x2 + lw, 10 + 2 * Ui.Step, 200, "U_OrdType", OrderTypes.List);
            U.Check("cActive", "Active", x + lw + 140, 10 + 2 * Ui.Step, 80, "U_Active");

            int ft = 70, top = ft + 25;
            AddFolder("fOps", "Opérations", x, ft, 110, 1);
            AddFolder("fComps", "Composants", x + 110, ft, 110, 2);
            AddFolder("fRem", "Remarques", x + 220, ft, 110, 3);
            U.Frame("rFrame", x, ft + 19, FormWidth - 30, FormHeight - ft - 110);
            int mh = FormHeight - top - 145, by = FormHeight - 140;

            U.Pane = 1;
            OpCompMatrices.BuildOperations(U, "mOps", Db.TaskOps, x + 10, top, FormWidth - 50, mh, false);
            U.Button("bOpAdd", "Ajouter une ligne", x + 10, by, 120);
            U.Button("bOpDel", "Supprimer la ligne", x + 135, by, 120);
            RegisterMatrix("mOps", Db.TaskOps, "U_Descr", "bOpAdd", "bOpDel", (ds, row) => OpCompMatrices.NewOperation(ds, row, H("U_WorkCtr")));
            RegisterCfl("mOps", "cWc", "@" + Db.TaskOps, "U_WorkCtr", "Code");
            RegisterCfl("mOps", "cVend", "@" + Db.TaskOps, "U_Vendor", "CardCode");

            U.Pane = 2;
            OpCompMatrices.BuildComponents(U, "mComps", Db.TaskComps, x + 10, top, FormWidth - 50, mh, false);
            U.Button("bCpAdd", "Ajouter une ligne", x + 10, by, 120);
            U.Button("bCpDel", "Supprimer la ligne", x + 135, by, 120);
            RegisterMatrix("mComps", Db.TaskComps, "U_ItemCode", "bCpAdd", "bCpDel", (ds, row) => OpCompMatrices.NewComponent(ds, row, SettingsService.Load().DefaultWarehouse));
            RegisterCfl("mComps", "cItem", "@" + Db.TaskComps, "U_ItemCode", "ItemCode");
            RegisterCfl("mComps", "cWhs", "@" + Db.TaskComps, "U_Whs", "WhsCode");

            U.Pane = 3;
            U.Memo("eRem", x + 10, top, FormWidth - 50, mh, "U_Remarks");
            U.Pane = 0;
        }

        protected override void SetDefaults()
        {
            SetH("U_Active", "Y");
            SetH("U_OrdType", OrderTypes.Preventive);
        }

        protected override string Validate()
        {
            if (H("Code") == "")
                return "Saisissez le code de la gamme.";
            if (H("Name") == "")
                return "Saisissez la désignation de la gamme.";
            if (Lines(Db.TaskOps).Size == 0)
                return "Une gamme comporte au moins une opération.";
            return OpCompMatrices.Validate(Lines(Db.TaskOps), Lines(Db.TaskComps));
        }

        protected override string CanDelete()
        {
            if (Sql.Exists("SELECT 1 FROM " + Db.T(Db.Plan) + " WHERE \"U_TaskList\" = " + Sql.Q(H("Code"))))
                return "La gamme est utilisée par un plan de maintenance.";
            return null;
        }

        protected override void OnChosen(string itemUid, string colUid, int row, DataTable selected)
        {
            if (itemUid == "mComps" && colUid == "cItem")
                OpCompMatrices.OnItemChosen(Lines(Db.TaskComps), row - 1, selected);
        }
    }
}
