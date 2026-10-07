using System;
using System.Globalization;
using System.Linq;
using SAPbouiCOM;
using MaintenanceAddon.Core;
using MaintenanceAddon.Models;
using MaintenanceAddon.Services;

namespace MaintenanceAddon.Forms
{
    /// <summary>
    /// Plan de maintenance préventive (IP41 temps / IP42 compteur) :
    /// cycle, horizon d'appel, base d'ordonnancement, historique des appels.
    /// </summary>
    internal sealed class PlanForm : UdoForm
    {
        private const string DtCalls = "dtCalls";

        public PlanForm(Application app) : base(app) { }

        public override string ObjectCode => Obj.Plan;
        protected override string FormType => FormIds.PlanForm;
        protected override string HeaderTable => Db.Plan;
        protected override string[] ChildTables => new string[0];
        protected override bool IsDocument => false;
        protected override string Title => "Plan de maintenance";
        protected override int FormWidth => 800;
        protected override int FormHeight => 600;

        protected override string CanOpen()
        {
            return AuthService.Denied(Perm.MasterData, Access.Read, "consulter les plans de maintenance");
        }

        protected override string CanEdit()
        {
            return AuthService.Denied(Perm.MasterData, Access.Full, "modifier les plans de maintenance");
        }

        protected override void Build()
        {
            const int lw = 130, x = 10, x2 = 410;
            BuildKeyFields(x, 10, lw);
            U.Label("lType", "Type de plan", x2, 10, lw, "cType");
            U.Combo("cType", x2 + lw, 10, 200, "U_Type", PlanTypes.List);
            U.Check("cActive", "Actif", x2 + lw, 10 + Ui.Step, 80, "U_Active");

            int y = 10 + 2 * Ui.Step + 8;
            U.Cfl("cflEq", Obj.Equip);
            Ui.BindCfl(U.Field("lEq", "Équipement", "eEq", x, y, lw, 120, "U_Equip"), "cflEq", "Code");
            RegisterCfl("eEq", null, "@" + Db.Plan, "U_Equip", "Code");
            RegisterUdoLink("kEq", "eEq", Obj.Equip);
            AddName("udEqNm", "eEqNm", x + lw + 125, y);

            U.Cfl("cflFl", Obj.FuncLoc);
            Ui.BindCfl(U.Field("lFl", "Poste technique", "eFl", x2 + 120, y, 90, 120, "U_FuncLoc"), "cflFl", "Code");
            RegisterCfl("eFl", null, "@" + Db.Plan, "U_FuncLoc", "Code");
            y += Ui.Step;

            U.Cfl("cflTsk", Obj.TaskList);
            Ui.BindCfl(U.Field("lTsk", "Gamme", "eTsk", x, y, lw, 120, "U_TaskList"), "cflTsk", "Code");
            RegisterCfl("eTsk", null, "@" + Db.Plan, "U_TaskList", "Code");
            RegisterUdoLink("kTsk", "eTsk", Obj.TaskList);
            AddName("udTskNm", "eTskNm", x + lw + 125, y);
            y += Ui.Step;

            U.Label("lOType", "Type d'ordre", x, y, lw, "cOType");
            U.Combo("cOType", x + lw, y, 180, "U_OrdType", OrderTypes.List);
            U.Label("lPrio", "Priorité", x2, y, lw, "cPrio");
            U.Combo("cPrio", x2 + lw, y, 150, "U_Priority", Priorities.List);
            y += Ui.Step;

            U.Cfl("cflWc", Obj.WorkCtr);
            Ui.BindCfl(U.Field("lWc", "Poste de travail", "eWc", x, y, lw, 120, "U_WorkCtr"), "cflWc", "Code");
            RegisterCfl("eWc", null, "@" + Db.Plan, "U_WorkCtr", "Code");
            U.Label("lBasis", "Échéance suivante depuis", x2, y, lw, "cBasis");
            U.Combo("cBasis", x2 + lw, y, 200, "U_Basis", SchedBasis.List);
            y += Ui.Step + 10;

            // ---- Cycle calendaire ----
            U.Label("lTime", "Cycle calendaire", x, y, 200);
            U.Label("lCnt", "Cycle compteur", x2, y, 200);
            y += Ui.Step;
            U.Field("lCycle", "Tous les", "eCycle", x, y, lw, 60, "U_Cycle");
            U.Combo("cUnit", x + lw + 65, y, 100, "U_CycUnit", CycleUnits.List);
            U.Field("lPoint", "Point de mesure", "ePoint", x2, y, lw, 80, "U_Point");
            F.DataSources.UserDataSources.Add("udPts", BoDataType.dt_SHORT_TEXT, 200);
            U.ReadOnlyUds("ePts", x2 + lw + 85, y, 160, "udPts");
            y += Ui.Step;
            U.Field("lLead", "Horizon d'appel (jours)", "eLead", x, y, lw, 60, "U_LeadDays");
            U.Field("lCycC", "Tous les (compteur)", "eCycC", x2, y, lw, 80, "U_CycCount");
            y += Ui.Step;
            U.Field("lStart", "Début du cycle", "eStart", x, y, lw, 100, "U_StartDate");
            U.Field("lLeadC", "Horizon d'appel (cpt)", "eLeadC", x2, y, lw, 80, "U_LeadCnt");
            y += Ui.Step;
            U.Field("lNext", "Prochaine échéance", "eNext", x, y, lw, 100, "U_NextDate");
            U.Field("lStartC", "Compteur de début", "eStartC", x2, y, lw, 80, "U_StartCnt");
            y += Ui.Step;
            U.Field("lLast", "Dernière réalisation", "eLast", x, y, lw, 100, "U_LastDate");
            U.Editable("eLast", false, true, false);
            U.Field("lNextC", "Prochaine échéance (cpt)", "eNextC", x2, y, lw, 80, "U_NextCnt");
            y += Ui.Step;
            U.Field("lLastC", "Dernier compteur réalisé", "eLastC", x2, y, lw, 80, "U_LastCnt");
            U.Editable("eLastC", false, true, false);
            F.DataSources.UserDataSources.Add("udCur", BoDataType.dt_SHORT_TEXT, 100);
            U.ReadOnlyUds("eCur", x, y, 330, "udCur");
            y += Ui.Step + 6;

            U.Label("lRem", "Remarques", x, y, lw, "eRem");
            U.Memo("eRem", x + lw, y, FormWidth - lw - 40, 34, "U_Remarks");
            y += 42;

            U.Label("lCalls", "Appels du plan (double-clic : ouvrir l'ordre)", x, y, 400);
            y += Ui.Step;
            U.Grid("gCalls", DtCalls, x, y, FormWidth - 30, FormHeight - y - 75);
            RegisterGrid("gCalls", DtCalls, Obj.Order);

            U.Button("bCall", "Appeler maintenant", 150, FormHeight - 62, 140);
            U.Button("bSkip", "Ignorer l'échéance", 295, FormHeight - 62, 140);
        }

        private void AddName(string uds, string id, int left, int top)
        {
            F.DataSources.UserDataSources.Add(uds, BoDataType.dt_SHORT_TEXT, 100);
            U.ReadOnlyUds(id, left, top, 150, uds);
        }

        protected override void SetDefaults()
        {
            SetH("U_Type", PlanTypes.Time);
            SetH("U_Active", "Y");
            SetH("U_OrdType", OrderTypes.Preventive);
            SetH("U_Priority", "3");
            SetH("U_Basis", SchedBasis.Planned);
            SetH("U_Cycle", "1");
            SetH("U_CycUnit", "M");
            SetH("U_LeadDays", "7");
            SetH("U_StartDate", DateTime.Today);
        }

        protected override string Validate()
        {
            if (H("Code") == "")
                return "Saisissez le code du plan.";
            if (H("Name") == "")
                return "Saisissez la désignation du plan (elle sert de description aux ordres).";
            string equip = H("U_Equip");
            if (equip == "" && H("U_FuncLoc") == "")
                return "Renseignez l'équipement ou le poste technique entretenu.";
            if (H("U_TaskList") != "" && !Sql.Exists("SELECT 1 FROM " + Db.T(Db.TaskList) + " WHERE \"Code\" = " + Sql.Q(H("U_TaskList"))))
                return "Gamme inconnue : " + H("U_TaskList");

            if (H("U_Type") == PlanTypes.Counter)
            {
                if (equip == "")
                    return "Un plan sur compteur porte sur un équipement.";
                MeasuringPoint mp = MeasurementService.Point(equip, H("U_Point"));
                if (mp == null || !mp.IsCounter)
                    return "Indiquez un point de mesure de type compteur de l'équipement (" + CounterPoints(equip) + ").";
                if (HDbl("U_CycCount") <= 0)
                    return "Saisissez l'intervalle du compteur (ex. toutes les 500 h).";
                if (HDbl("U_NextCnt") <= 0)
                    SetH("U_NextCnt", HDbl("U_StartCnt") + HDbl("U_CycCount"));
            }
            else
            {
                if (HDbl("U_Cycle") <= 0 || H("U_CycUnit") == "")
                    return "Saisissez le cycle (ex. tous les 3 mois).";
                if (HDate("U_StartDate") == null)
                    return "Saisissez la date de début du cycle.";
                if (HDate("U_NextDate") == null)
                    SetH("U_NextDate", PlanService.AddCycle(HDate("U_StartDate").Value, (int)HDbl("U_Cycle"), H("U_CycUnit")));
            }
            return null;
        }

        protected override string CanDelete()
        {
            if (Sql.Exists("SELECT 1 FROM " + Db.T(Db.Call) + " WHERE \"U_PlanCode\" = " + Sql.Q(H("Code"))))
                return "Le plan a déjà été appelé : désactivez-le au lieu de le supprimer.";
            return null;
        }

        private static string CounterPoints(string equip)
        {
            var pts = MeasurementService.Points(equip).Where(p => p.IsCounter).Select(p => p.Point + " (" + p.Descr + ")").ToList();
            return pts.Count == 0 ? "aucun compteur défini sur l'équipement" : "compteurs : " + string.Join(", ", pts);
        }

        protected override void Refresh()
        {
            string equip = H("U_Equip");
            SetUds("udEqNm", NotificationService.NameOf(Db.Equip, equip));
            SetUds("udTskNm", NotificationService.NameOf(Db.TaskList, H("U_TaskList")));
            bool counter = H("U_Type") == PlanTypes.Counter;
            SetUds("udPts", counter && equip != "" ? CounterPoints(equip) : "");
            if (counter && equip != "" && H("U_Point") != "")
            {
                double cur = MeasurementService.CurrentCounter(equip, H("U_Point"));
                SetUds("udCur", "Compteur actuel : " + cur.ToString("N2", CultureInfo.GetCultureInfo("fr-FR")));
            }
            else
                SetUds("udCur", "");

            foreach (string id in new[] { "eCycle", "cUnit", "eLead", "eStart", "eNext" })
                Enable(id, !counter);
            foreach (string id in new[] { "ePoint", "eCycC", "eLeadC", "eStartC", "eNextC" })
                Enable(id, counter);

            string key = CurrentKey;
            U.LoadGrid("gCalls", DtCalls, ReportService.PlanCallsSql(key == "" ? "\u0001" : key));
            bool active = key != "" && H("U_Active") != "N";
            Enable("bCall", active);
            Enable("bSkip", active);
        }

        protected override void OnComboSelect(string itemUid, string colUid, int row)
        {
            if (itemUid == "cType")
                Refresh();
        }

        protected override void OnChosen(string itemUid, string colUid, int row, DataTable selected)
        {
            if (itemUid == "eEq")
            {
                EquipmentInfo eq = EquipmentInfo.Load(H("U_Equip"));
                if (eq != null)
                {
                    SetH("U_FuncLoc", eq.FuncLoc);
                    if (H("U_WorkCtr") == "")
                        SetH("U_WorkCtr", eq.WorkCtr);
                }
            }
        }

        protected override void OnButton(string itemUid)
        {
            if (itemUid == "bCall")
            {
                if (!RequireSaved())
                    return;
                string plan = CurrentKey;
                if (!Confirm("Créer maintenant l'ordre préventif du plan " + plan + " ?"))
                    return;
                int order = PlanService.Call(plan);
                Reload();
                Msg("Ordre " + OrderService.DocNum(order) + " créé.");
                Navigator.Open(Obj.Order, order.ToString(CultureInfo.InvariantCulture));
            }
            else if (itemUid == "bSkip")
            {
                if (!RequireSaved())
                    return;
                string plan = CurrentKey;
                if (!Confirm("Ignorer la prochaine échéance du plan " + plan + " (aucun ordre ne sera créé) ?"))
                    return;
                PlanService.Skip(plan);
                Reload();
                Msg("Échéance ignorée ; prochaine échéance recalculée.");
            }
        }
    }
}
