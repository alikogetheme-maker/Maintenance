using System;
using System.Globalization;
using SAPbouiCOM;
using MaintenanceAddon.Core;
using MaintenanceAddon.Models;
using MaintenanceAddon.Services;

namespace MaintenanceAddon.Forms
{
    /// <summary>
    /// Avis de maintenance (IW21 / IW22 / IW23) : demande, panne ou rapport
    /// d'activité ; codes de catalogue ; durée d'arrêt ; création de l'ordre.
    /// </summary>
    internal sealed class NotificationForm : UdoForm
    {
        public NotificationForm(Application app) : base(app) { }

        public override string ObjectCode => Obj.Notif;
        protected override string FormType => FormIds.NotifForm;
        protected override string HeaderTable => Db.Notif;
        protected override string[] ChildTables => new string[0];
        protected override bool IsDocument => true;
        protected override string Title => "Avis de maintenance";
        protected override int FormWidth => 780;
        protected override int FormHeight => 560;

        protected override void Build()
        {
            const int lw = 110, x = 10, x2 = 400;
            int y = 10;
            BuildKeyFields(x, y, lw);
            U.Label("lType", "Type d'avis", x2, y, lw, "cType");
            U.Combo("cType", x2 + lw, y, 230, "U_Type", NotifTypes.List);
            y += Ui.Step;
            U.Label("lStatus", "Statut", x2, y, lw, "cStatus");
            U.Combo("cStatus", x2 + lw, y, 230, "U_Status", NotifStatus.List);
            U.Editable("cStatus", false, true, false);
            y += Ui.Step;
            U.Label("lPrio", "Priorité", x, y, lw, "cPrio");
            U.Combo("cPrio", x + lw, y, 150, "U_Priority", Priorities.List);
            U.Field("lOrder", "Ordre", "eOrder", x2, y, lw, 80, "U_OrderNo");
            U.Editable("eOrder", false, true, false);
            RegisterUdoLink("kOrder", "eOrder", Obj.Order);
            y += Ui.Step + 8;

            U.Cfl("cflEq", Obj.Equip);
            Ui.BindCfl(U.Field("lEq", "Équipement", "eEq", x, y, lw, 120, "U_Equip"), "cflEq", "Code");
            RegisterCfl("eEq", null, "@" + Db.Notif, "U_Equip", "Code");
            RegisterUdoLink("kEq", "eEq", Obj.Equip);
            AddName("udEqNm", "eEqNm", x + lw + 125, y, 150);
            U.Field("lRepD", "Déclaré le", "eRepD", x2, y, lw, 90, "U_RepDate");
            U.Edit("eRepT", x2 + lw + 95, y, 50, "U_RepTime");
            y += Ui.Step;

            U.Cfl("cflFl", Obj.FuncLoc);
            Ui.BindCfl(U.Field("lFl", "Poste technique", "eFl", x, y, lw, 120, "U_FuncLoc"), "cflFl", "Code");
            RegisterCfl("eFl", null, "@" + Db.Notif, "U_FuncLoc", "Code");
            RegisterUdoLink("kFl", "eFl", Obj.FuncLoc);
            AddName("udFlNm", "eFlNm", x + lw + 125, y, 150);
            U.Field("lRepBy", "Déclaré par", "eRepBy", x2, y, lw, 145, "U_ReportBy");
            y += Ui.Step;

            U.Cfl("cflWc", Obj.WorkCtr);
            Ui.BindCfl(U.Field("lWc", "Poste de travail", "eWc", x, y, lw, 120, "U_WorkCtr"), "cflWc", "Code");
            RegisterCfl("eWc", null, "@" + Db.Notif, "U_WorkCtr", "Code");
            U.Field("lReqS", "Début souhaité", "eReqS", x2, y, lw, 90, "U_ReqStart");
            y += Ui.Step;
            U.Field("lPoint", "Point de mesure", "ePoint", x, y, lw, 80, "U_Point");
            U.Editable("ePoint", false, true, false);
            U.Field("lReqE", "Fin souhaitée", "eReqE", x2, y, lw, 90, "U_ReqEnd");
            y += Ui.Step + 4;

            U.Field("lSubj", "Description courte", "eSubj", x, y, lw, FormWidth - lw - 40, "U_Subject");
            y += Ui.Step;
            F.DataSources.UserDataSources.Add("udCtx", BoDataType.dt_LONG_TEXT, 254);
            U.ReadOnlyUds("eCtx", x + lw, y, FormWidth - lw - 40, "udCtx");
            y += Ui.Step + 6;

            int ft = y;
            AddFolder("fDescr", "Description", x, ft, 110, 1);
            AddFolder("fBrk", "Panne / arrêt", x + 110, ft, 110, 2);
            AddFolder("fCat", "Codes catalogue", x + 220, ft, 120, 3);
            int top = ft + 25;
            U.Frame("rFrame", x, ft + 19, FormWidth - 30, FormHeight - ft - 110);

            U.Pane = 1;
            U.Memo("eDescr", x + 10, top, FormWidth - 50, FormHeight - top - 100, "U_Descr");

            U.Pane = 2;
            y = top;
            U.Check("cBrk", "Arrêt de l'équipement (panne)", x + 10, y, 250, "U_Breakdwn");
            y += Ui.Step + 6;
            U.Field("lMSt", "Début de panne", "eMStD", x + 10, y, lw + 20, 90, "U_MalfStD");
            U.Edit("eMStT", x + 10 + lw + 115, y, 50, "U_MalfStT");
            y += Ui.Step;
            U.Field("lMEn", "Fin de panne", "eMEnD", x + 10, y, lw + 20, 90, "U_MalfEnD");
            U.Edit("eMEnT", x + 10 + lw + 115, y, 50, "U_MalfEnT");
            y += Ui.Step + 6;
            F.DataSources.UserDataSources.Add("udDown", BoDataType.dt_SHORT_TEXT, 100);
            U.ReadOnlyUds("eDown", x + 10, y, 400, "udDown");

            U.Pane = 3;
            y = top;
            AddCatalog("lObj", "Partie d'objet", "cObj", "U_ObjPart", CatalogTypes.ObjectPart, x + 10, y);
            AddCatalog("lDam", "Dommage", "cDam", "U_Damage", CatalogTypes.Damage, x + 10, y + Ui.Step);
            AddCatalog("lCau", "Cause", "cCau", "U_Cause", CatalogTypes.Cause, x + 10, y + 2 * Ui.Step);
            AddCatalog("lAct", "Activité réalisée", "cAct", "U_Activity", CatalogTypes.Activity, x + 10, y + 3 * Ui.Step);
            U.Pane = 0;

            U.Button("bOrder", "Créer l'ordre", 150, FormHeight - 62, 110);
            U.Button("bDone", "Terminer l'avis", 265, FormHeight - 62, 110);
            U.Button("bReopen", "Rouvrir l'avis", 380, FormHeight - 62, 110);
        }

        private void AddName(string uds, string id, int left, int top, int width)
        {
            F.DataSources.UserDataSources.Add(uds, BoDataType.dt_SHORT_TEXT, 100);
            U.ReadOnlyUds(id, left, top, width, uds);
        }

        private void AddCatalog(string labelId, string caption, string id, string alias, string type, int left, int top)
        {
            U.Label(labelId, caption, left, top, 130, id);
            ComboBox c = U.Combo(id, left + 130, top, 300, alias, null, true);
            foreach (var v in NotificationService.CatalogCodes(type))
                c.ValidValues.Add(v.Key, v.Key + " - " + v.Value);
        }

        /// <summary>Nouvel avis pré-rempli (depuis un équipement ou un relevé hors limites).</summary>
        public void NewFor(string equip, string point, string subject)
        {
            ShowNew(() =>
            {
                ApplyEquipment(equip);
                if (!string.IsNullOrEmpty(point))
                    SetH("U_Point", point);
                if (!string.IsNullOrEmpty(subject))
                    SetH("U_Subject", NotificationService.Truncate(subject, 100));
            });
        }

        private void ApplyEquipment(string equip)
        {
            EquipmentInfo eq = EquipmentInfo.Load(equip);
            if (eq == null)
                return;
            SetH("U_Equip", eq.Code);
            SetH("U_FuncLoc", eq.FuncLoc);
            if (H("U_WorkCtr") == "")
                SetH("U_WorkCtr", eq.WorkCtr);
        }

        protected override void SetDefaults()
        {
            SetH("U_Type", NotifTypes.Malfunction);
            SetH("U_Status", NotifStatus.Outstanding);
            SetH("U_Priority", "3");
            SetH("U_Breakdwn", "N");
            SetH("U_RepDate", DateTime.Today);
            SetH("U_RepTime", DateTime.Now.ToString("HHmm", CultureInfo.InvariantCulture));
            SetH("U_ReportBy", DiCompany.UserCode);
            ApplyPriority();
        }

        /// <summary>Dates souhaitées calculées depuis la priorité (comme le profil de priorité SAP).</summary>
        private void ApplyPriority()
        {
            DateTime start = HDate("U_ReqStart") ?? DateTime.Today;
            int hours = SettingsService.Load().HoursFor(H("U_Priority"));
            SetH("U_ReqStart", start);
            SetH("U_ReqEnd", start.Date.Add(DateTime.Now.TimeOfDay).AddHours(hours).Date);
        }

        protected override string Validate()
        {
            if (H("U_Equip") == "" && H("U_FuncLoc") == "")
                return "Indiquez l'équipement ou le poste technique concerné.";
            if (H("U_Equip") != "" && EquipmentInfo.Load(H("U_Equip")) == null)
                return "Équipement inconnu : " + H("U_Equip");
            if (H("U_Subject") == "")
                return "Saisissez la description courte de l'avis.";
            if (H("U_Type") == "" || H("U_Priority") == "")
                return "Renseignez le type et la priorité de l'avis.";
            DateTime? rs = HDate("U_ReqStart"), re = HDate("U_ReqEnd");
            if (rs.HasValue && re.HasValue && re < rs)
                return "La fin souhaitée précède le début souhaité.";
            if (H("U_Breakdwn") == "Y")
            {
                if (HDate("U_MalfStD") == null)
                    return "Panne : saisissez la date de début de l'arrêt (onglet Panne / arrêt).";
                DateTime? st = Stamp("U_MalfStD", "U_MalfStT"), en = Stamp("U_MalfEnD", "U_MalfEnT");
                if (en.HasValue && en < st)
                    return "La fin de panne précède son début.";
            }

            // Garantie / contrat à la date de déclaration, mémorisés sur l'avis
            if (F.Mode == BoFormMode.fm_ADD_MODE)
            {
                ServiceContext ctx = H("U_Equip") == "" ? null : ServiceContext.For(H("U_Equip"), HDate("U_RepDate") ?? DateTime.Today);
                SetH("U_UnderWar", ctx != null && ctx.UnderWarranty ? "Y" : "N");
                SetH("U_Contract", ctx?.Contract?.Code ?? "");
            }
            return null;
        }

        private DateTime? Stamp(string dateAlias, string timeAlias)
        {
            DateTime? d = HDate(dateAlias);
            if (d == null)
                return null;
            int.TryParse(H(timeAlias).Replace(":", ""), out int t);
            return d.Value.AddHours(t / 100).AddMinutes(t % 100);
        }

        protected override void Refresh()
        {
            SetUds("udEqNm", NotificationService.NameOf(Db.Equip, H("U_Equip")));
            SetUds("udFlNm", NotificationService.NameOf(Db.FuncLoc, H("U_FuncLoc")));
            ServiceContext ctx = H("U_Equip") == "" ? null : ServiceContext.For(H("U_Equip"), HDate("U_RepDate") ?? DateTime.Today);
            string banner = ctx?.Banner() ?? "";
            SetUds("udCtx", banner);
            if (banner != "" && F.Mode == BoFormMode.fm_ADD_MODE)
                Msg(banner, BoStatusBarMessageType.smt_Warning);

            DateTime? st = Stamp("U_MalfStD", "U_MalfStT"), en = Stamp("U_MalfEnD", "U_MalfEnT");
            if (H("U_Breakdwn") == "Y" && st.HasValue)
            {
                double h = ((en ?? DateTime.Now) - st.Value).TotalHours;
                SetUds("udDown", "Durée d'arrêt : " + Math.Max(0, h).ToString("N1", CultureInfo.GetCultureInfo("fr-FR")) + " h" + (en.HasValue ? "" : " (panne en cours)"));
            }
            else
                SetUds("udDown", "");

            bool saved = CurrentKey != "";
            string status = H("U_Status");
            int order = (int)HDbl("U_OrderNo");
            Enable("bOrder", saved && status != NotifStatus.Completed && order == 0 && H("U_Type") != NotifTypes.Activity);
            Enable("bDone", saved && status != NotifStatus.Completed);
            Enable("bReopen", saved && status == NotifStatus.Completed);
        }

        protected override void OnComboSelect(string itemUid, string colUid, int row)
        {
            if (itemUid == "cPrio" && F.Mode != BoFormMode.fm_FIND_MODE)
                ApplyPriority();
        }

        protected override void OnValidate(string itemUid, string colUid, int row)
        {
            if (itemUid == "eMStD" || itemUid == "eMEnD" || itemUid == "eMStT" || itemUid == "eMEnT")
                Refresh();
        }

        protected override void OnChosen(string itemUid, string colUid, int row, DataTable selected)
        {
            if (itemUid == "eEq")
                ApplyEquipment(H("U_Equip"));
        }

        protected override void OnButton(string itemUid)
        {
            if ((itemUid != "bOrder" && itemUid != "bDone" && itemUid != "bReopen") || !RequireSaved())
                return;
            int entry = CurrentDocEntry;
            switch (itemUid)
            {
                case "bOrder":
                    {
                        int order = NotificationService.CreateOrder(entry);
                        Reload();
                        Msg("Ordre " + OrderService.DocNum(order) + " créé à partir de l'avis.");
                        Navigator.Open(Obj.Order, order.ToString(CultureInfo.InvariantCulture));
                        break;
                    }
                case "bDone":
                    if (!Confirm("Terminer l'avis (statut NOCO) ?"))
                        return;
                    NotificationService.Complete(entry, DateTime.Today);
                    Reload();
                    Msg("Avis terminé.");
                    break;
                case "bReopen":
                    NotificationService.Reopen(entry);
                    Reload();
                    Msg("Avis rouvert.");
                    break;
            }
        }
    }
}
