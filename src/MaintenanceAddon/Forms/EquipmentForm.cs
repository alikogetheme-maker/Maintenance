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
    /// Fiche équipement (IE01 / IE02) : données générales, techniques et
    /// d'achat liées à l'article et au n° de série SAP, pièces de rechange,
    /// points de mesure, documents joints, historique (avis, ordres, coûts, pannes).
    /// </summary>
    internal sealed class EquipmentForm : UdoForm
    {
        private const string DtHist = "dtHist";
        private const string DtAtc = "dtAtc";
        private bool _assets;

        public EquipmentForm(Application app) : base(app) { }

        public override string ObjectCode => Obj.Equip;
        protected override string FormType => FormIds.EquipForm;
        protected override string HeaderTable => Db.Equip;
        protected override string[] ChildTables => new[] { Db.EquipPts, Db.EquipParts };
        protected override bool IsDocument => false;
        protected override string Title => "Équipement";
        protected override int FormWidth => 800;
        protected override int FormHeight => 580;

        protected override string CanOpen()
        {
            return AuthService.Denied(Perm.MasterData, Access.Read, "consulter les équipements");
        }

        protected override string CanEdit()
        {
            return AuthService.Denied(Perm.MasterData, Access.Full, "modifier les équipements");
        }

        protected override void Build()
        {
            const int lw = 120, x = 10, x2 = 420;
            _assets = EquipmentService.FixedAssetsUsed();
            BuildKeyFields(x, 10, lw);

            U.Label("lCat", "Catégorie", x2, 10, 100, "cCat");
            ComboBox cat = U.Combo("cCat", x2 + 100, 10, 200, "U_Category", null, true);
            foreach (Row r in Sql.Rows("SELECT \"Code\", \"Name\" FROM " + Db.T(Db.EqCat) + " ORDER BY \"Code\""))
                cat.ValidValues.Add(r.Str("Code"), r.Str("Name"));
            U.Label("lStatus", "Statut", x2, 10 + Ui.Step, 100, "cStatus");
            U.Combo("cStatus", x2 + 100, 10 + Ui.Step, 200, "U_Status", EquipStatus.List);
            U.Label("lCrit", "Criticité", x2, 10 + 2 * Ui.Step, 100, "cCrit");
            U.Combo("cCrit", x2 + 100, 10 + 2 * Ui.Step, 200, "U_Critic", Criticality.List, true);

            int ft = 70;
            AddFolder("fGen", "Général", x, ft, 90, 1);
            AddFolder("fTech", "Données techniques", x + 90, ft, 125, 2);
            AddFolder("fParts", "Pièces de rechange", x + 215, ft, 120, 6);
            AddFolder("fPts", "Points de mesure", x + 335, ft, 115, 3);
            AddFolder("fDocs", "Documents", x + 450, ft, 90, 7);
            AddFolder("fHist", "Historique", x + 540, ft, 85, 4);
            AddFolder("fRem", "Remarques", x + 625, ft, 85, 5);
            int top = ft + 25;
            int by = FormHeight - 140;
            U.Frame("rFrame", x, ft + 19, FormWidth - 30, FormHeight - ft - 110);

            // ---- Onglet Général ---------------------------------------------
            U.Pane = 1;
            int y = top;
            U.Cfl("cflFl", Obj.FuncLoc);
            Ui.BindCfl(U.Field("lFl", "Poste technique", "eFl", x + 10, y, lw, 120, "U_FuncLoc"), "cflFl", "Code");
            RegisterCfl("eFl", null, "@" + Db.Equip, "U_FuncLoc", "Code");
            AddNameField("udFlNm", "eFlNm", x + 10 + lw + 125, y);
            RegisterUdoLink("kFl", "eFl", Obj.FuncLoc);
            y += Ui.Step;

            U.Cfl("cflPar", Obj.Equip);
            Ui.BindCfl(U.Field("lPar", "Équipement supérieur", "ePar", x + 10, y, lw, 120, "U_Parent"), "cflPar", "Code");
            RegisterCfl("ePar", null, "@" + Db.Equip, "U_Parent", "Code");
            AddNameField("udParNm", "eParNm", x + 10 + lw + 125, y);
            RegisterUdoLink("kPar", "ePar", Obj.Equip);
            y += Ui.Step;

            U.Cfl("cflWc", Obj.WorkCtr);
            Ui.BindCfl(U.Field("lWc", "Poste de travail", "eWc", x + 10, y, lw, 120, "U_WorkCtr"), "cflWc", "Code");
            RegisterCfl("eWc", null, "@" + Db.Equip, "U_WorkCtr", "Code");
            AddNameField("udWcNm", "eWcNm", x + 10 + lw + 125, y);
            y += Ui.Step;

            ChooseFromList cflOcr = U.Cfl("cflOcr", "61");
            Ui.CflFilter(cflOcr, "DimCode", SettingsService.Load().Dimension.ToString(CultureInfo.InvariantCulture));
            Ui.BindCfl(U.Field("lOcr", "Centre de coûts", "eOcr", x + 10, y, lw, 120, "U_OcrCode"), "cflOcr", "PrcCode");
            RegisterCfl("eOcr", null, "@" + Db.Equip, "U_OcrCode", "PrcCode");
            y += Ui.Step;

            U.Cfl("cflWhs", "64");
            Ui.BindCfl(U.Field("lWhs", "Magasin de pièces", "eWhs", x + 10, y, lw, 120, "U_Whs"), "cflWhs", "WhsCode");
            RegisterCfl("eWhs", null, "@" + Db.Equip, "U_Whs", "WhsCode");
            U.LinkStd("kWhs", "eWhs", BoLinkedObject.lf_Warehouses);
            y += Ui.Step;

            U.Field("lStart", "Mise en service", "eStart", x + 10, y, lw, 120, "U_StartUp");
            y += Ui.Step + 10;
            F.DataSources.UserDataSources.Add("udCtx", BoDataType.dt_LONG_TEXT, 254);
            U.ReadOnlyUds("eCtx", x + 10, y, FormWidth - 60, "udCtx");
            F.DataSources.UserDataSources.Add("udShip", BoDataType.dt_LONG_TEXT, 254);
            U.ReadOnlyUds("eShip", x + 10, y + Ui.Step, FormWidth - 60, "udShip");

            // ---- Onglet Données techniques ----------------------------------
            U.Pane = 2;
            y = top;
            U.Field("lManuf", "Fabricant", "eManuf", x + 10, y, lw, 200, "U_Manuf");
            U.Field("lAcqD", "Date d'acquisition", "eAcqD", x2, y, lw, 120, "U_AcqDate");
            y += Ui.Step;
            U.Field("lModel", "Modèle", "eModel", x + 10, y, lw, 200, "U_Model");
            U.Field("lAcqV", "Valeur d'acquisition", "eAcqV", x2, y, lw, 120, "U_AcqValue");
            y += Ui.Step;
            U.Field("lSerial", "N° de série", "eSerial", x + 10, y, lw, 150, "U_SerialNo");
            U.Button("bSerial", "N° reçus...", x + 10 + lw + 155, y - 2, 80);
            U.Field("lWarr", "Fin de garantie", "eWarr", x2, y, lw, 120, "U_WarrEnd");
            y += Ui.Step;
            U.Field("lYear", "Année de construction", "eYear", x + 10, y, lw, 80, "U_ConstYear");
            F.DataSources.UserDataSources.Add("udWarr", BoDataType.dt_SHORT_TEXT, 60);
            U.ReadOnlyUds("eWarrSt", x2 + lw, y, 200, "udWarr");
            y += Ui.Step + 6;

            // Article SAP de l'équipement (articles « normaux », pas les immobilisations)
            ChooseFromList cflItem = U.Cfl("cflItem", "4");
            Ui.CflFilter(cflItem, "ItemType", "I");
            Ui.BindCfl(U.Field("lItem", "Article SAP", "eItem", x + 10, y, lw, 120, "U_ItemCode"), "cflItem", "ItemCode");
            RegisterCfl("eItem", null, "@" + Db.Equip, "U_ItemCode", "ItemCode");
            U.LinkStd("kItem", "eItem", BoLinkedObject.lf_Items);
            AddNameField("udItemNm", "eItemNm", x + 10 + lw + 125, y);
            y += Ui.Step;

            // Immobilisation : seulement si le module Immobilisations de SAP est utilisé
            if (_assets)
            {
                ChooseFromList cflAsset = U.Cfl("cflAsset", "4");
                Ui.CflFilter(cflAsset, "ItemType", "F");
                Ui.BindCfl(U.Field("lAsset", "Immobilisation SAP", "eAsset", x + 10, y, lw, 120, "U_AssetNo"), "cflAsset", "ItemCode");
                RegisterCfl("eAsset", null, "@" + Db.Equip, "U_AssetNo", "ItemCode");
                AddNameField("udAsNm", "eAsNm", x + 10 + lw + 125, y);
                y += Ui.Step;
            }

            ChooseFromList cflVend = U.Cfl("cflVend", "2");
            Ui.CflFilter(cflVend, "CardType", "S");
            Ui.BindCfl(U.Field("lVend", "Fournisseur d'achat", "eVend", x + 10, y, lw, 120, "U_Vendor"), "cflVend", "CardCode");
            RegisterCfl("eVend", null, "@" + Db.Equip, "U_Vendor", "CardCode");
            U.LinkStd("kVend", "eVend", BoLinkedObject.lf_BusinessPartner);
            AddNameField("udVendNm", "eVendNm", x + 10 + lw + 125, y);
            y += Ui.Step;

            ChooseFromList cflWv = U.Cfl("cflWVd", "2");
            Ui.CflFilter(cflWv, "CardType", "S");
            Ui.BindCfl(U.Field("lWVd", "Garant (si différent)", "eWVd", x + 10, y, lw, 120, "U_WarrVend"), "cflWVd", "CardCode");
            RegisterCfl("eWVd", null, "@" + Db.Equip, "U_WarrVend", "CardCode");
            U.LinkStd("kWVd", "eWVd", BoLinkedObject.lf_BusinessPartner);
            AddNameField("udWVdNm", "eWVdNm", x + 10 + lw + 125, y);
            y += Ui.Step;

            ChooseFromList cflMv = U.Cfl("cflMVd", "2");
            Ui.CflFilter(cflMv, "CardType", "S");
            Ui.BindCfl(U.Field("lMVd", "Prestataire de maintenance", "eMVd", x + 10, y, lw, 120, "U_MntVend"), "cflMVd", "CardCode");
            RegisterCfl("eMVd", null, "@" + Db.Equip, "U_MntVend", "CardCode");
            U.LinkStd("kMVd", "eMVd", BoLinkedObject.lf_BusinessPartner);
            AddNameField("udMVdNm", "eMVdNm", x + 10 + lw + 125, y);
            y += Ui.Step + 8;
            F.DataSources.UserDataSources.Add("udSrc", BoDataType.dt_LONG_TEXT, 254);
            U.ReadOnlyUds("eSrc", x + 10, y, FormWidth - 60, "udSrc");

            // ---- Onglet Pièces de rechange ----------------------------------
            U.Pane = 6;
            Matrix mp = U.Matrix("mParts", x + 10, top, FormWidth - 60, FormHeight - top - 145);
            string pp = "@" + Db.EquipParts;
            U.Col(mp, "#", "#", 25, pp, "LineId", false);
            ChooseFromList cflPart = U.Cfl("cflPIt", "4");
            Ui.CflFilter(cflPart, "ItemType", "I");
            Column pItem = U.Col(mp, "cItem", "Article", 110, pp, "U_ItemCode", true, BoFormItemTypes.it_LINKED_BUTTON);
            ((LinkedButton)pItem.ExtendedObject).LinkedObject = BoLinkedObject.lf_Items;
            Ui.BindCfl(pItem, "cflPIt", "ItemCode");
            U.Col(mp, "cName", "Désignation", 250, pp, "U_ItemName", true);
            U.Col(mp, "cQty", "Qté montée", 80, pp, "U_Qty", true);
            U.Col(mp, "cRem", "Remarque", 220, pp, "U_Remarks", true);
            U.Button("bPaAdd", "Ajouter une ligne", x + 10, by, 120);
            U.Button("bPaDel", "Supprimer la ligne", x + 135, by, 120);
            U.Label("lPaInfo", "Articles SAP à avoir en stock pour cet équipement : proposés lors de la préparation des ordres.", x + 260, by + 2, 470);
            RegisterMatrix("mParts", Db.EquipParts, "U_ItemCode", "bPaAdd", "bPaDel", (ds, row) => ds.SetValue("U_Qty", row, "1"));
            RegisterCfl("mParts", "cItem", pp, "U_ItemCode", "ItemCode");

            // ---- Onglet Documents (pièces jointes SAP) ----------------------
            U.Pane = 7;
            U.Grid("gAtc", DtAtc, x + 10, top, FormWidth - 60, FormHeight - top - 145);
            U.Button("bAtAdd", "Joindre un document...", x + 10, by, 150);
            U.Button("bAtOpen", "Ouvrir", x + 165, by, 90);
            U.Button("bAtDel", "Retirer", x + 260, by, 90);
            U.Label("lAtInfo", "Notices, photos, schémas, certificats : copiés dans le dossier des pièces jointes de SAP.", x + 360, by + 2, 400);

            // ---- Onglet Points de mesure ------------------------------------
            U.Pane = 3;
            Matrix m = U.Matrix("mPts", x + 10, top, FormWidth - 60, FormHeight - top - 145);
            string pts = "@" + Db.EquipPts;
            U.Col(m, "#", "#", 25, pts, "LineId", false);
            U.Col(m, "cPoint", "Point", 80, pts, "U_Point", true);
            U.Col(m, "cDescr", "Description", 170, pts, "U_Descr", true);
            U.Col(m, "cUnit", "Unité", 60, pts, "U_Unit", true);
            Column cnt = U.Col(m, "cCount", "Compteur", 65, pts, "U_Counter", true, BoFormItemTypes.it_CHECK_BOX);
            cnt.ValOn = "Y";
            cnt.ValOff = "N";
            U.Col(m, "cAnn", "Estim. annuelle", 90, pts, "U_AnnEst", true);
            U.Col(m, "cMin", "Limite basse", 80, pts, "U_MinVal", true);
            U.Col(m, "cMax", "Limite haute", 80, pts, "U_MaxVal", true);
            U.Col(m, "cProd", "Par la production", 100, pts, "U_ProdCnt", true, BoFormItemTypes.it_CHECK_BOX);
            U.Button("bPtAdd", "Ajouter une ligne", x + 10, by, 120);
            U.Button("bPtDel", "Supprimer la ligne", x + 135, by, 120);
            U.Button("bReading", "Saisir un relevé...", x + 260, by, 130);
            RegisterMatrix("mPts", Db.EquipPts, "U_Point", "bPtAdd", "bPtDel", (ds, row) =>
            {
                ds.SetValue("U_Counter", row, "N");
                ds.SetValue("U_ProdCnt", row, "N");
            });

            // ---- Onglet Historique ------------------------------------------
            U.Pane = 4;
            F.DataSources.UserDataSources.Add("udKpi", BoDataType.dt_LONG_TEXT, 254);
            U.ReadOnlyUds("eKpi", x + 10, top, FormWidth - 60, "udKpi");
            U.Grid("gHist", DtHist, x + 10, top + 22, FormWidth - 60, FormHeight - top - 145);
            RegisterGrid("gHist", DtHist, null);

            // ---- Onglet Remarques -------------------------------------------
            U.Pane = 5;
            U.Memo("eRem", x + 10, top, FormWidth - 60, FormHeight - top - 145, "U_Remarks");

            U.Pane = 0;
            U.Button("bNotif", "Créer un avis", 150, FormHeight - 62, 110);
            U.Button("bOrders", "Ordres de l'équipement", 265, FormHeight - 62, 150);
            U.Button("bShip", "Envoi / retour prestataire", 420, FormHeight - 62, 165);
        }

        private void AddNameField(string uds, string id, int left, int top)
        {
            F.DataSources.UserDataSources.Add(uds, BoDataType.dt_SHORT_TEXT, 100);
            U.ReadOnlyUds(id, left, top, 220, uds);
        }

        /// <summary>Nouvel équipement installé sur un poste technique.</summary>
        public void NewAt(string funcLoc)
        {
            ShowNew(() => SetH("U_FuncLoc", funcLoc));
        }

        protected override void SetDefaults()
        {
            SetH("Code", EquipmentService.NextCode());
            SetH("U_Status", EquipStatus.Active);
            SetH("U_StartUp", DateTime.Today);
        }

        /// <summary>
        /// Reprend sur la fiche le n° de série reçu dans SAP : article, n°, fabricant,
        /// fournisseur, date et valeur d'achat, fin de garantie (en création : nouvel équipement).
        /// </summary>
        private void ApplySerial(SerialInfo s)
        {
            if (F == null || F.Mode == BoFormMode.fm_FIND_MODE)
                return;
            if (s.Equipment != "" && s.Equipment != H("Code"))
            {
                Msg("Ce n° de série est déjà rattaché à l'équipement " + s.Equipment + ".", BoStatusBarMessageType.smt_Error);
                return;
            }
            foreach (var f in EquipmentService.FieldsFrom(s))
            {
                if (f.Value is DateTime d)
                    SetH(f.Key, d);
                else if (f.Value is string text)
                {
                    // Fabricant et fournisseur saisis à la main : conservés
                    if ((f.Key == "U_Manuf" || f.Key == "U_Vendor") && H(f.Key) != "")
                        continue;
                    SetH(f.Key, text);
                }
                else
                    SetH(f.Key, Convert.ToDouble(f.Value, CultureInfo.InvariantCulture));
            }
            if (H("Name") == "")
                SetH("Name", NotificationService.Truncate(s.ItemName, 100));
            SetModeUpdate();
            Refresh();
            F.Select();
            Msg("N° de série " + s.Serial + " repris" + (s.Source() == "" ? "." : " (" + s.Source() + ")."));
        }

        protected override string Validate()
        {
            string code = H("Code");
            if (code == "")
                return "Saisissez le code de l'équipement.";
            if (H("Name") == "")
                return "Saisissez la désignation de l'équipement.";
            if (H("U_FuncLoc") != "" && !Sql.Exists("SELECT 1 FROM " + Db.T(Db.FuncLoc) + " WHERE \"Code\" = " + Sql.Q(H("U_FuncLoc"))))
                return "Poste technique inconnu : " + H("U_FuncLoc");
            string p = H("U_Parent");
            for (int i = 0; i < 30 && p != ""; i++)
            {
                if (p == code)
                    return "L'équipement supérieur crée une boucle dans la hiérarchie.";
                p = Sql.ScalarStr("SELECT \"U_Parent\" FROM " + Db.T(Db.Equip) + " WHERE \"Code\" = " + Sql.Q(p));
            }
            string links = EquipmentService.CheckLinks(code, H("U_ItemCode"), H("U_SerialNo"), H("U_Manuf"), H("U_AssetNo"));
            if (links != null)
                return links;
            // N° de série saisi à la main : rattaché au n° reçu dans SAP s'il existe
            SerialInfo serial = EquipmentService.Serial(H("U_ItemCode"), H("U_SerialNo"));
            SetH("U_SerSys", serial == null ? 0 : serial.SysNumber);

            DBDataSource parts = Lines(Db.EquipParts);
            var items = new HashSet<string>();
            for (int i = 0; i < parts.Size; i++)
            {
                string item = parts.GetValue("U_ItemCode", i).Trim();
                if (!Sql.Exists("SELECT 1 FROM \"OITM\" WHERE \"ItemCode\" = " + Sql.Q(item)))
                    return "Pièce de rechange : article inconnu (" + item + ").";
                if (!items.Add(item))
                    return "Pièce de rechange : l'article " + item + " est en double.";
                if (Sql.ParseDouble(parts.GetValue("U_Qty", i)) < 0)
                    return "Pièce de rechange " + item + " : quantité négative.";
            }

            DBDataSource ds = Lines(Db.EquipPts);
            var seen = new HashSet<string>();
            for (int i = 0; i < ds.Size; i++)
            {
                string point = ds.GetValue("U_Point", i).Trim();
                if (!seen.Add(point))
                    return "Le point de mesure « " + point + " » est en double.";
                double min = Sql.ParseDouble(ds.GetValue("U_MinVal", i)), max = Sql.ParseDouble(ds.GetValue("U_MaxVal", i));
                if (min != 0 && max != 0 && min > max)
                    return "Point « " + point + " » : la limite basse dépasse la limite haute.";
                if (ds.GetValue("U_ProdCnt", i).Trim() == "Y" && ds.GetValue("U_Counter", i).Trim() != "Y")
                    return "Point « " + point + " » : seul un compteur peut être alimenté par la production (cochez « Compteur »).";
            }
            return null;
        }

        protected override string CanDelete()
        {
            string code = H("Code");
            if (Sql.Exists("SELECT 1 FROM " + Db.T(Db.Order) + " WHERE \"U_Equip\" = " + Sql.Q(code)) ||
                Sql.Exists("SELECT 1 FROM " + Db.T(Db.Notif) + " WHERE \"U_Equip\" = " + Sql.Q(code)))
                return "L'équipement a un historique (avis, ordres) : passez-le au statut « Mis au rebut » au lieu de le supprimer.";
            if (Sql.Exists("SELECT 1 FROM " + Db.T(Db.Plan) + " WHERE \"U_Equip\" = " + Sql.Q(code)))
                return "L'équipement est utilisé par un plan de maintenance.";
            if (Sql.Exists("SELECT 1 FROM " + Db.T(Db.Equip) + " WHERE \"U_Parent\" = " + Sql.Q(code)))
                return "Des équipements dépendent de cet équipement.";
            return null;
        }

        protected override string CanDeleteLine(string table, DBDataSource ds, int row)
        {
            string point = ds.GetValue("U_Point", row).Trim();
            if (point != "" && Sql.Exists("SELECT 1 FROM " + Db.T(Db.MeasDoc) + " WHERE \"U_Equip\" = " + Sql.Q(H("Code")) + " AND \"U_Point\" = " + Sql.Q(point)))
                return "Des relevés existent pour le point « " + point + " » : il ne peut pas être supprimé.";
            return null;
        }

        protected override void Refresh()
        {
            SetUds("udFlNm", NotificationService.NameOf(Db.FuncLoc, H("U_FuncLoc")));
            SetUds("udParNm", NotificationService.NameOf(Db.Equip, H("U_Parent")));
            SetUds("udWcNm", NotificationService.NameOf(Db.WorkCtr, H("U_WorkCtr")));
            SetUds("udItemNm", H("U_ItemCode") == "" ? "" : Sql.ScalarStr("SELECT \"ItemName\" FROM \"OITM\" WHERE \"ItemCode\" = " + Sql.Q(H("U_ItemCode"))));
            if (_assets)
                SetUds("udAsNm", H("U_AssetNo") == "" ? "" : Sql.ScalarStr("SELECT \"ItemName\" FROM \"OITM\" WHERE \"ItemCode\" = " + Sql.Q(H("U_AssetNo"))));
            SerialInfo serial = EquipmentService.Serial(H("U_ItemCode"), H("U_SerialNo"));
            SetUds("udSrc", serial == null
                ? (H("U_ItemCode") != "" && H("U_SerialNo") != "" ? "N° de série non reçu dans SAP pour cet article (saisi à la main)." : "")
                : "N° de série SAP" + (serial.MnfSerial != "" ? " (n° fabricant " + serial.MnfSerial + ")" : "") +
                  (serial.Source() == "" ? "" : " reçu par " + serial.Source()) + ".");
            SetUds("udVendNm", ServiceContext.VendorName(H("U_Vendor")));
            SetUds("udWVdNm", ServiceContext.VendorName(H("U_WarrVend")));
            SetUds("udMVdNm", ServiceContext.VendorName(H("U_MntVend")));
            ServiceContext ctx = CurrentKey == "" ? null : ServiceContext.For(CurrentKey, DateTime.Today);
            string banner = ctx?.Banner() ?? "";
            if (ctx != null && ctx.Contract == null && ctx.Equipment.MaintVendor != "")
                banner += (banner == "" ? "" : "  |  ") + "Prestataire attitré : " + ServiceContext.VendorName(ctx.Equipment.MaintVendor);
            SetUds("udCtx", banner);
            SetUds("udShip", CurrentKey == "" ? "" : ShipmentService.Describe(ShipmentService.OpenShipment(CurrentKey)));
            DateTime? warr = HDate("U_WarrEnd");
            SetUds("udWarr", warr == null ? "" : warr.Value >= DateTime.Today ? "Sous garantie" : "Garantie expirée");

            string key = CurrentKey;
            U.LoadGrid("gHist", DtHist, ReportService.EquipmentHistorySql(key == "" ? "\u0001" : key));
            SetUds("udKpi", key == "" ? "" : Summary(key));
            U.LoadGrid("gAtc", DtAtc, AttachmentService.FilesSql(key == "" ? -1 : (int)HDbl("U_AtcEntry")));

            bool saved = key != "";
            Enable("bNotif", saved);
            Enable("bOrders", saved);
            Enable("bReading", saved);
            Enable("bShip", saved);
            Enable("bAtAdd", saved);
            Enable("bAtOpen", saved && HDbl("U_AtcEntry") > 0);
            Enable("bAtDel", saved && HDbl("U_AtcEntry") > 0);
            Enable("bSerial", F.Mode != BoFormMode.fm_FIND_MODE);
        }

        /// <summary>N° de ligne (ATC1) du document sélectionné dans la grille, 0 si aucun.</summary>
        private int SelectedAttachment()
        {
            Grid g = (Grid)F.Items.Item("gAtc").Specific;
            DataTable dt = F.DataSources.DataTables.Item(DtAtc);
            if (Ui.IsEmpty(dt))
                return 0;
            int row = -1;
            if (g.Rows.SelectedRows.Count > 0)
                row = g.GetDataTableRowIndex(g.Rows.SelectedRows.Item(0, BoOrderType.ot_RowOrder));
            else if (dt.Rows.Count == 1)
                row = 0;
            if (row < 0 || row >= dt.Rows.Count)
                return 0;
            return Convert.ToInt32(dt.GetValue("Key", row), CultureInfo.InvariantCulture);
        }

        /// <summary>Synthèse sur 12 mois glissants : ordres, pannes, arrêt, coûts.</summary>
        private static string Summary(string equip)
        {
            var f = new ReportFilter { From = DateTime.Today.AddYears(-1).AddDays(1), To = DateTime.Today, Equip = equip };
            ReportView kpi = ReportService.View("KPI");
            Row r = Sql.First(kpi.Sql(f));
            if (r == null)
                return "12 derniers mois : aucun ordre ni panne.";
            return "12 derniers mois : " + r.Int("NbOrd") + " ordre(s), " + r.Int("NbPan") + " panne(s), arrêt " +
                   r.Dbl("ArretH").ToString("N1", CultureInfo.GetCultureInfo("fr-FR")) + " h, MTTR " +
                   r.Dbl("MTTR").ToString("N1", CultureInfo.GetCultureInfo("fr-FR")) + " h, disponibilité " +
                   r.Dbl("Dispo").ToString("N1", CultureInfo.GetCultureInfo("fr-FR")) + " %, coût total " + Sql.Amount(r.Dbl("CoutTot")) + ".";
        }

        protected override void OnChosen(string itemUid, string colUid, int row, DataTable selected)
        {
            if (itemUid == "mParts" && colUid == "cItem")
                Lines(Db.EquipParts).SetValue("U_ItemName", row - 1, Convert.ToString(selected.GetValue("ItemName", 0), CultureInfo.InvariantCulture));
            // Article choisi sur une nouvelle fiche : désignation proposée
            if (itemUid == "eItem" && H("Name") == "")
                SetH("Name", NotificationService.Truncate(Convert.ToString(selected.GetValue("ItemName", 0), CultureInfo.InvariantCulture), 100));
            // Équipement installé sur un poste : reprise des données d'imputation si vides
            if (itemUid == "eFl")
            {
                Row fl = Sql.First("SELECT \"U_OcrCode\", \"U_Whs\" FROM " + Db.T(Db.FuncLoc) + " WHERE \"Code\" = " + Sql.Q(H("U_FuncLoc")));
                if (fl != null)
                {
                    if (H("U_OcrCode") == "") SetH("U_OcrCode", fl.Str("U_OcrCode"));
                    if (H("U_Whs") == "") SetH("U_Whs", fl.Str("U_Whs"));
                }
            }
        }

        protected override void OnButton(string itemUid)
        {
            switch (itemUid)
            {
                case "bNotif":
                    if (RequireSaved())
                        Navigator.Notification.NewFor(CurrentKey, null, null);
                    break;
                case "bOrders":
                    if (RequireSaved())
                        Program_ShowList("ORD", CurrentKey);
                    break;
                case "bReading":
                    if (RequireSaved())
                        Navigator.Measurement.Show(CurrentKey, null);
                    break;
                case "bShip":
                    // Le statut de l'équipement change : on recharge la fiche après l'opération
                    if (RequireSaved())
                        Navigator.Shipment.Show(CurrentKey, 0, Reload);
                    break;
                case "bSerial":
                    Navigator.Serials.Show(H("U_ItemCode"), ApplySerial);
                    break;
                case "bAtAdd":
                    {
                        if (!RequireSaved())
                            return;
                        string file = Program.PickFile("Joindre un document à l'équipement " + CurrentKey);
                        if (string.IsNullOrEmpty(file))
                            return;
                        AttachmentService.AddToEquipment(CurrentKey, file);
                        Reload();
                        SelectFolder("fDocs");
                        Msg("Document « " + System.IO.Path.GetFileName(file) + " » joint à l'équipement.");
                        break;
                    }
                case "bAtOpen":
                    {
                        int line = SelectedAttachment();
                        if (line == 0)
                        {
                            Msg("Sélectionnez d'abord le document dans la liste.", BoStatusBarMessageType.smt_Warning);
                            return;
                        }
                        string path = AttachmentService.FullPath((int)HDbl("U_AtcEntry"), line);
                        if (path == null || !System.IO.File.Exists(path))
                        {
                            Program.Message(App, "Fichier introuvable : " + path);
                            return;
                        }
                        Program.OpenFile(path);
                        break;
                    }
                case "bAtDel":
                    {
                        if (!RequireSaved())
                            return;
                        int line = SelectedAttachment();
                        if (line == 0)
                        {
                            Msg("Sélectionnez d'abord le document dans la liste.", BoStatusBarMessageType.smt_Warning);
                            return;
                        }
                        if (!Confirm("Retirer ce document de la fiche de l'équipement ?\n(Le fichier reste dans le dossier des pièces jointes de SAP.)"))
                            return;
                        AttachmentService.RemoveFromEquipment(CurrentKey, line);
                        Reload();
                        SelectFolder("fDocs");
                        Msg("Document retiré.");
                        break;
                    }
            }
        }

        private static void Program_ShowList(string view, string equip)
        {
            ListForm.Instance?.Show(view, equip);
        }
    }
}
