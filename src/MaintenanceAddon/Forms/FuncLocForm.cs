using SAPbouiCOM;
using MaintenanceAddon.Core;
using MaintenanceAddon.Services;

namespace MaintenanceAddon.Forms
{
    /// <summary>Poste technique (IL01 / IL02) : structure hiérarchique des sites, lignes, zones.</summary>
    internal sealed class FuncLocForm : UdoForm
    {
        private const string DtContent = "dtCont";

        public FuncLocForm(Application app) : base(app) { }

        public override string ObjectCode => Obj.FuncLoc;
        protected override string FormType => FormIds.FuncLocForm;
        protected override string HeaderTable => Db.FuncLoc;
        protected override string[] ChildTables => new string[0];
        protected override bool IsDocument => false;
        protected override string Title => "Poste technique";
        protected override int FormWidth => 660;
        protected override int FormHeight => 520;

        protected override string CanOpen()
        {
            return AuthService.Denied(Perm.MasterData, Access.Read, "consulter les postes techniques");
        }

        protected override string CanEdit()
        {
            return AuthService.Denied(Perm.MasterData, Access.Full, "modifier les postes techniques");
        }

        protected override void Build()
        {
            const int lw = 130, x = 10;
            int y = 10;
            BuildKeyFields(x, y, lw);
            y += 2 * Ui.Step + 6;

            U.Cfl("cflPar", Obj.FuncLoc);
            Ui.BindCfl(U.Field("lParent", "Poste supérieur", "eParent", x, y, lw, 120, "U_Parent"), "cflPar", "Code");
            RegisterCfl("eParent", null, "@" + Db.FuncLoc, "U_Parent", "Code");
            F.DataSources.UserDataSources.Add("udParNm", BoDataType.dt_SHORT_TEXT, 100);
            U.ReadOnlyUds("eParNm", x + lw + 125, y, 260, "udParNm");
            RegisterUdoLink("kParent", "eParent", Obj.FuncLoc);
            y += Ui.Step;

            U.Field("lLoc", "Emplacement / adresse", "eLoc", x, y, lw, 385, "U_Location");
            y += Ui.Step;

            ChooseFromList cflOcr = U.Cfl("cflOcr", "61");
            Ui.CflFilter(cflOcr, "DimCode", SettingsService.Load().Dimension.ToString());
            Ui.BindCfl(U.Field("lOcr", "Centre de coûts", "eOcr", x, y, lw, 120, "U_OcrCode"), "cflOcr", "PrcCode");
            RegisterCfl("eOcr", null, "@" + Db.FuncLoc, "U_OcrCode", "PrcCode");
            y += Ui.Step;

            U.Cfl("cflWhs", "64");
            Ui.BindCfl(U.Field("lWhs", "Magasin de pièces", "eWhs", x, y, lw, 120, "U_Whs"), "cflWhs", "WhsCode");
            RegisterCfl("eWhs", null, "@" + Db.FuncLoc, "U_Whs", "WhsCode");
            U.LinkStd("kWhs", "eWhs", BoLinkedObject.lf_Warehouses);
            U.Check("cActive", "Actif", x + lw + 140, y, 100, "U_Active");
            U.Check("cLine", "Ligne de production", x + lw + 245, y, 160, "U_IsLine");
            y += Ui.Step + 4;

            U.Label("lRem", "Remarques", x, y, lw, "eRem");
            U.Memo("eRem", x + lw, y, 385, 40, "U_Remarks");
            y += 50;

            U.Label("lCont", "Contenu (double-clic pour ouvrir)", x, y, 300);
            y += Ui.Step;
            U.Grid("gCont", DtContent, x, y, 630, FormHeight - y - 75);
            RegisterGrid("gCont", DtContent, null);
            U.Button("bNewEq", "Nouvel équipement ici", 150, FormHeight - 62, 150);
        }

        protected override void SetDefaults()
        {
            SetH("U_Active", "Y");
            SetH("U_IsLine", "N");
        }

        protected override string Validate()
        {
            string code = H("Code");
            if (code == "")
                return "Saisissez le code du poste technique.";
            if (H("Name") == "")
                return "Saisissez la désignation du poste technique.";
            string parent = H("U_Parent");
            if (parent != "")
            {
                if (!Sql.Exists("SELECT 1 FROM " + Db.T(Db.FuncLoc) + " WHERE \"Code\" = " + Sql.Q(parent)))
                    return "Poste technique supérieur inconnu : " + parent;
                // Pas de boucle dans la hiérarchie
                string p = parent;
                for (int i = 0; i < 30 && p != ""; i++)
                {
                    if (p == code)
                        return "Le poste supérieur crée une boucle dans la hiérarchie.";
                    p = Sql.ScalarStr("SELECT \"U_Parent\" FROM " + Db.T(Db.FuncLoc) + " WHERE \"Code\" = " + Sql.Q(p));
                }
            }
            return null;
        }

        protected override string CanDelete()
        {
            string code = H("Code");
            if (Sql.Exists("SELECT 1 FROM " + Db.T(Db.FuncLoc) + " WHERE \"U_Parent\" = " + Sql.Q(code)))
                return "Des postes techniques dépendent de ce poste : rattachez-les ailleurs d'abord.";
            if (Sql.Exists("SELECT 1 FROM " + Db.T(Db.Equip) + " WHERE \"U_FuncLoc\" = " + Sql.Q(code)))
                return "Des équipements sont installés sur ce poste : désinstallez-les d'abord.";
            if (Sql.Exists("SELECT 1 FROM " + Db.T(Db.Order) + " WHERE \"U_FuncLoc\" = " + Sql.Q(code)))
                return "Le poste technique est utilisé dans des ordres : désactivez-le plutôt que de le supprimer.";
            return null;
        }

        protected override void Refresh()
        {
            SetUds("udParNm", NotificationService.NameOf(Db.FuncLoc, H("U_Parent")));
            U.LoadGrid("gCont", DtContent, ReportService.FuncLocContentSql(CurrentKey == "" ? "\u0001" : CurrentKey));
            Enable("bNewEq", CurrentKey != "");
        }

        protected override void OnButton(string itemUid)
        {
            if (itemUid == "bNewEq" && RequireSaved())
            {
                string fl = CurrentKey;
                Navigator.Equipment.NewAt(fl);
            }
        }
    }
}
