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
    /// Contrat de maintenance avec un prestataire : période, préavis,
    /// montant et facturation, délai d'intervention, couverture, équipements
    /// couverts, ordres et factures du contrat.
    /// </summary>
    internal sealed class ContractForm : UdoForm
    {
        private const string DtAct = "dtAct";

        public ContractForm(Application app) : base(app) { }

        public override string ObjectCode => Obj.Contract;
        protected override string FormType => FormIds.ContractForm;
        protected override string HeaderTable => Db.Contract;
        protected override string[] ChildTables => new[] { Db.ContractEq };
        protected override bool IsDocument => false;
        protected override string Title => "Contrat de maintenance";
        protected override int FormWidth => 820;
        protected override int FormHeight => 580;

        protected override string CanOpen()
        {
            return AuthService.Denied(Perm.MasterData, Access.Read, "consulter les contrats de maintenance");
        }

        protected override string CanEdit()
        {
            return AuthService.Denied(Perm.MasterData, Access.Full, "modifier les contrats de maintenance");
        }

        protected override void Build()
        {
            const int lw = 130, x = 10, x2 = 420;
            BuildKeyFields(x, 10, lw);
            U.Label("lType", "Type de contrat", x2, 10, lw, "cType");
            U.Combo("cType", x2 + lw, 10, 230, "U_Type", ContractTypes.List);
            U.Check("cActive", "Actif", x2 + lw, 10 + Ui.Step, 80, "U_Active");

            int y = 10 + 2 * Ui.Step + 6;
            ChooseFromList cflV = U.Cfl("cflVend", "2");
            Ui.CflFilter(cflV, "CardType", "S");
            Ui.BindCfl(U.Field("lVend", "Prestataire", "eVend", x, y, lw, 110, "U_Vendor"), "cflVend", "CardCode");
            RegisterCfl("eVend", null, "@" + Db.Contract, "U_Vendor", "CardCode");
            U.LinkStd("kVend", "eVend", BoLinkedObject.lf_BusinessPartner);
            F.DataSources.UserDataSources.Add("udVendNm", BoDataType.dt_SHORT_TEXT, 100);
            U.ReadOnlyUds("eVendNm", x + lw + 115, y, 150, "udVendNm");
            U.Field("lRef", "Référence prestataire", "eRef", x2, y, lw, 150, "U_RefExt");
            y += Ui.Step;
            U.Field("lStart", "Début", "eStart", x, y, lw, 90, "U_StartDt");
            U.Field("lAmt", "Montant annuel HT", "eAmt", x2, y, lw, 120, "U_Amount");
            y += Ui.Step;
            U.Field("lEnd", "Fin", "eEnd", x, y, lw, 90, "U_EndDt");
            U.Label("lBill", "Facturation", x2, y, lw, "cBill");
            U.Combo("cBill", x2 + lw, y, 150, "U_Billing", BillingPeriods.List);
            y += Ui.Step;
            U.Field("lNotice", "Préavis (jours)", "eNotice", x, y, lw, 60, "U_Notice");
            U.Field("lResp", "Délai d'intervention (h)", "eResp", x2, y, lw, 60, "U_RespHrs");
            y += Ui.Step;
            U.Field("lVisits", "Visites préventives / an", "eVisits", x, y, lw, 60, "U_Visits");
            U.Check("cLab", "Main-d'oeuvre incluse", x2, y, 180, "U_CovLab");
            U.Check("cParts", "Pièces incluses", x2 + 190, y, 150, "U_CovParts");
            y += Ui.Step + 4;
            F.DataSources.UserDataSources.Add("udSum", BoDataType.dt_LONG_TEXT, 254);
            U.ReadOnlyUds("eSum", x, y, FormWidth - 40, "udSum");
            y += Ui.Step + 8;

            int ft = y, top = ft + 25;
            AddFolder("fEq", "Équipements couverts", x, ft, 150, 1);
            AddFolder("fAct", "Ordres et factures", x + 150, ft, 130, 2);
            AddFolder("fRem", "Remarques", x + 280, ft, 100, 3);
            U.Frame("rFrame", x, ft + 19, FormWidth - 30, FormHeight - ft - 110);
            int mh = FormHeight - top - 125;

            U.Pane = 1;
            Matrix m = U.Matrix("mEq", x + 10, top, FormWidth - 50, mh);
            string t = "@" + Db.ContractEq;
            U.Col(m, "#", "#", 25, t, "LineId", false);
            U.Cfl("cflEq", Obj.Equip);
            Ui.BindCfl(U.Col(m, "cEq", "Équipement", 120, t, "U_Equip", true), "cflEq", "Code");
            U.Col(m, "cName", "Désignation", 350, t, "U_EqName", false);
            U.Button("bEqAdd", "Ajouter une ligne", x + 10, FormHeight - 120, 120);
            U.Button("bEqDel", "Supprimer la ligne", x + 135, FormHeight - 120, 120);
            RegisterMatrix("mEq", Db.ContractEq, "U_Equip", "bEqAdd", "bEqDel");
            RegisterCfl("mEq", "cEq", t, "U_Equip", "Code");

            U.Pane = 2;
            U.Grid("gAct", DtAct, x + 10, top, FormWidth - 50, mh + 25);
            RegisterGrid("gAct", DtAct, null);

            U.Pane = 3;
            U.Memo("eRem", x + 10, top, FormWidth - 50, mh + 25, "U_Remarks");
            U.Pane = 0;
        }

        protected override void SetDefaults()
        {
            SetH("U_Type", "C");
            SetH("U_Active", "Y");
            SetH("U_Billing", "Y");
            SetH("U_CovLab", "Y");
            SetH("U_CovParts", "N");
            SetH("U_Notice", "90");
            SetH("U_StartDt", new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));
            SetH("U_EndDt", new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddYears(1).AddDays(-1));
        }

        protected override string Validate()
        {
            string code = H("Code");
            if (code == "" || H("Name") == "")
                return "Saisissez le code et la désignation du contrat.";
            if (!Sql.Exists("SELECT 1 FROM \"OCRD\" WHERE \"CardType\" = 'S' AND \"CardCode\" = " + Sql.Q(H("U_Vendor"))))
                return "Indiquez le prestataire (fournisseur) du contrat.";
            DateTime? s = HDate("U_StartDt"), e = HDate("U_EndDt");
            if (s == null || e == null)
                return "Renseignez les dates de début et de fin du contrat.";
            if (e < s)
                return "La fin du contrat précède son début.";
            if (HDbl("U_Amount") < 0 || HDbl("U_RespHrs") < 0)
                return "Montant et délai d'intervention ne peuvent pas être négatifs.";

            DBDataSource ds = Lines(Db.ContractEq);
            var seen = new HashSet<string>();
            bool active = H("U_Active") != "N";
            for (int i = 0; i < ds.Size; i++)
            {
                string eq = ds.GetValue("U_Equip", i).Trim();
                if (!seen.Add(eq))
                    return "L'équipement " + eq + " figure deux fois.";
                if (EquipmentInfo.Load(eq) == null)
                    return "Équipement inconnu : " + eq;
                if (!active)
                    continue;
                // Un seul contrat actif par équipement sur une même période
                string other = Sql.ScalarStr(
                    "SELECT TOP 1 c.\"Code\" FROM " + Db.T(Db.Contract) + " c JOIN " + Db.T(Db.ContractEq) + " l ON l.\"Code\" = c.\"Code\" " +
                    "WHERE l.\"U_Equip\" = " + Sql.Q(eq) + " AND c.\"Code\" <> " + Sql.Q(code) + " AND ISNULL(c.\"U_Active\", 'Y') = 'Y' " +
                    "AND ISNULL(c.\"U_StartDt\", '19000101') <= " + Sql.D(e.Value) + " AND ISNULL(c.\"U_EndDt\", '29991231') >= " + Sql.D(s.Value));
                if (other != "")
                    return "L'équipement " + eq + " est déjà couvert par le contrat actif " + other + " sur cette période.";
            }
            return null;
        }

        protected override string CanDelete()
        {
            if (Sql.Exists("SELECT 1 FROM " + Db.T(Db.Order) + " WHERE \"U_Contract\" = " + Sql.Q(H("Code"))))
                return "Des ordres sont rattachés au contrat : désactivez-le au lieu de le supprimer.";
            return null;
        }

        protected override void Refresh()
        {
            SetUds("udVendNm", ServiceContext.VendorName(H("U_Vendor")));
            string key = CurrentKey;
            U.LoadGrid("gAct", DtAct, ReportService.ContractActivitySql(key == "" ? "\u0001" : key));
            SetUds("udSum", key == "" ? "" : Summary(key));
        }

        private string Summary(string code)
        {
            ContractInfo c = ContractService.Load(code);
            if (c == null)
                return "";
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            double amount = HDbl("U_Amount");
            double expected = ContractService.ExpectedToDate(c, amount, DateTime.Today);
            double invoiced = ContractService.Invoiced(code);
            string end = "";
            if (c.End.HasValue)
            {
                int days = (int)(c.End.Value - DateTime.Today).TotalDays;
                DateTime notice = c.End.Value.AddDays(-(int)HDbl("U_Notice"));
                end = days < 0 ? "Contrat échu depuis " + (-days) + " j"
                    : "Fin dans " + days + " j, résiliation avant le " + notice.ToString("dd/MM/yyyy") + (notice < DateTime.Today ? " (préavis dépassé)" : "");
            }
            int perYear = BillingPeriods.PerYear(H("U_Billing"));
            return end + "  |  Prévu à date " + expected.ToString("N0", fr) + "  |  Facturé " + invoiced.ToString("N0", fr) +
                   "  |  Écart " + (invoiced - expected).ToString("N0", fr) +
                   (amount > 0 ? "  |  Échéance de facturation " + (amount / perYear).ToString("N0", fr) : "");
        }

        protected override void OnChosen(string itemUid, string colUid, int row, DataTable selected)
        {
            if (itemUid == "mEq" && colUid == "cEq")
                Lines(Db.ContractEq).SetValue("U_EqName", row - 1, Convert.ToString(selected.GetValue("Name", 0), CultureInfo.InvariantCulture));
        }
    }
}
