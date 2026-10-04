using System;
using System.Globalization;
using SAPbouiCOM;
using MaintenanceAddon.Core;
using MaintenanceAddon.Services;

namespace MaintenanceAddon.Forms
{
    /// <summary>
    /// Envoi d'un équipement en réparation chez un prestataire, puis retour.
    /// L'écran se met en mode « retour » si l'équipement est déjà parti.
    /// </summary>
    internal sealed class ShipmentForm : SimpleForm
    {
        private Action _onSaved;
        private bool _returnMode;

        public ShipmentForm(Application app) : base(app) { }

        protected override string FormType => FormIds.ShipForm;
        protected override string Title => "Envoi / retour chez un prestataire";
        protected override int FormWidth => 560;
        protected override int FormHeight => 330;

        public void Show(string equip, int orderEntry, Action onSaved)
        {
            _onSaved = onSaved;
            Open(true);
            if (!string.IsNullOrEmpty(equip))
                SetUds("udEq", equip);
            SetUds("udOrd", orderEntry > 0 ? orderEntry.ToString(CultureInfo.InvariantCulture) : "");
            LoadEquipment();
        }

        protected override void Build()
        {
            const int lw = 130, x = 10;
            int y = 10;
            AddUds("udEq", BoDataType.dt_SHORT_TEXT, 50);
            AddUds("udEqNm", BoDataType.dt_SHORT_TEXT, 100);
            AddUds("udInfo", BoDataType.dt_LONG_TEXT, 254);
            AddUds("udVend", BoDataType.dt_SHORT_TEXT, 15);
            AddUds("udVendNm", BoDataType.dt_SHORT_TEXT, 100);
            AddUds("udOrd", BoDataType.dt_SHORT_TEXT, 11);
            AddUds("udDate", BoDataType.dt_DATE);
            AddUds("udExp", BoDataType.dt_DATE);
            AddUds("udText", BoDataType.dt_SHORT_TEXT, 200);

            U.Cfl("cflEq", Obj.Equip);
            U.Label("lEq", "Équipement", x, y, lw, "eEq");
            Ui.BindCfl(U.EditUds("eEq", x + lw, y, 120, "udEq"), "cflEq", "Code");
            U.ReadOnlyUds("eEqNm", x + lw + 125, y, 270, "udEqNm");
            y += Ui.Step;
            U.ReadOnlyUds("eInfo", x + lw, y, 395, "udInfo");
            y += Ui.Step + 8;

            ChooseFromList cflV = U.Cfl("cflVend", "2");
            Ui.CflFilter(cflV, "CardType", "S");
            U.Label("lVend", "Prestataire", x, y, lw, "eVend");
            Ui.BindCfl(U.EditUds("eVend", x + lw, y, 120, "udVend"), "cflVend", "CardCode");
            U.ReadOnlyUds("eVendNm", x + lw + 125, y, 270, "udVendNm");
            y += Ui.Step;
            U.Label("lOrd", "Ordre (option)", x, y, lw, "eOrd");
            U.EditUds("eOrd", x + lw, y, 80, "udOrd");
            y += Ui.Step;
            U.Label("lDate", "Date", x, y, lw, "eDate");
            U.EditUds("eDate", x + lw, y, 100, "udDate");
            y += Ui.Step;
            U.Label("lExp", "Retour prévu", x, y, lw, "eExp");
            U.EditUds("eExp", x + lw, y, 100, "udExp");
            y += Ui.Step;
            U.Label("lText", "Motif / commentaire", x, y, lw, "eText");
            U.EditUds("eText", x + lw, y, 395, "udText");

            U.Button("bOk", "Enregistrer l'envoi", x, FormHeight - 62, 150);
            U.Button("bCancel", "Fermer", x + 155, FormHeight - 62, 90);
        }

        private void LoadEquipment()
        {
            string equip = Uds("udEq").Trim();
            SetUds("udEqNm", NotificationService.NameOf(Db.Equip, equip));
            SetUds("udDate", DateValue(DateTime.Today));
            SetUds("udText", "");
            Row open = equip == "" ? null : ShipmentService.OpenShipment(equip);
            _returnMode = open != null;
            if (_returnMode)
            {
                SetUds("udInfo", ShipmentService.Describe(open));
                SetUds("udVend", open.Str("U_Vendor"));
                SetUds("udOrd", open.Int("U_OrderNo") > 0 ? open.Int("U_OrderNo").ToString(CultureInfo.InvariantCulture) : "");
                SetUds("udExp", open.Date("U_ExpRet").HasValue ? DateValue(open.Date("U_ExpRet").Value) : "");
            }
            else
            {
                SetUds("udInfo", equip == "" ? "" : "Équipement sur site : saisissez l'envoi.");
                ServiceContext ctx = equip == "" ? null : ServiceContext.For(equip, DateTime.Today);
                if (Uds("udVend") == "" && ctx != null)
                    SetUds("udVend", ctx.DefaultVendor);
                SetUds("udExp", DateValue(DateTime.Today.AddDays(14)));
            }
            SetUds("udVendNm", ServiceContext.VendorName(Uds("udVend")));
            F.Title = _returnMode ? "Retour de chez le prestataire" : "Envoi chez un prestataire";
            ((Button)F.Items.Item("bOk").Specific).Caption = _returnMode ? "Enregistrer le retour" : "Enregistrer l'envoi";
            ((StaticText)F.Items.Item("lDate").Specific).Caption = _returnMode ? "Date de retour" : "Date d'envoi";
            foreach (string id in new[] { "eVend", "eOrd", "eExp" })
                F.Items.Item(id).Enabled = !_returnMode;
        }

        protected override void OnEvent(ItemEvent e)
        {
            switch (e.EventType)
            {
                case BoEventTypes.et_CHOOSE_FROM_LIST:
                    if (e.ItemUID == "eEq")
                    {
                        string code = Chosen(e, "Code");
                        if (code != null) { SetUds("udEq", code); SetUds("udVend", ""); LoadEquipment(); }
                    }
                    else if (e.ItemUID == "eVend")
                    {
                        string code = Chosen(e, "CardCode");
                        if (code != null) { SetUds("udVend", code); SetUds("udVendNm", ServiceContext.VendorName(code)); }
                    }
                    break;
                case BoEventTypes.et_VALIDATE:
                    if (e.ItemUID == "eEq" && e.ItemChanged)
                        LoadEquipment();
                    break;
                case BoEventTypes.et_ITEM_PRESSED:
                    if (!e.ActionSuccess)
                        break;
                    if (e.ItemUID == "bCancel")
                        Close();
                    else if (e.ItemUID == "bOk")
                        Save();
                    break;
            }
        }

        private void Save()
        {
            string equip = Uds("udEq").Trim();
            DateTime? date = Sql.ParseDate(Uds("udDate"));
            if (equip == "" || date == null)
            {
                Msg("Renseignez l'équipement et la date.", BoStatusBarMessageType.smt_Warning);
                return;
            }
            if (_returnMode)
            {
                ShipmentService.Return(equip, date.Value, Uds("udText"));
                Msg("Retour enregistré : l'équipement a repris son statut.");
            }
            else
            {
                int.TryParse(Uds("udOrd"), out int order);
                ShipmentService.Send(equip, Uds("udVend").Trim(), order, date.Value, Sql.ParseDate(Uds("udExp")), Uds("udText"));
                Msg("Envoi enregistré : l'équipement est « chez le prestataire ».");
            }
            Close();
            try
            {
                _onSaved?.Invoke();
            }
            catch (Exception ex)
            {
                Program.Message(App, "L'opération est enregistrée, mais l'écran appelant n'a pas pu être actualisé : " + ex.Message);
            }
        }
    }
}
