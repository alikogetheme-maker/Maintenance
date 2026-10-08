using System;
using System.Collections.Generic;
using SAPbouiCOM;
using MaintenanceAddon.Core;

namespace MaintenanceAddon.Forms
{
    /// <summary>
    /// Ouverture des écrans de l'add-on depuis n'importe quel autre écran
    /// (flèches de lien, double-clic dans les rapports, enchaînements).
    /// </summary>
    internal static class Navigator
    {
        private static readonly Dictionary<string, UdoForm> Forms = new Dictionary<string, UdoForm>();

        public static Application App;
        public static FuncLocForm FuncLoc;
        public static TaskListForm TaskList;
        public static PlanForm Plan;
        public static ContractForm Contract;
        public static SchedulingForm Scheduling;
        public static ListForm Lists;
        public static SettingsForm Settings;
        public static EquipmentForm Equipment;
        public static NotificationForm Notification;
        public static OrderForm Order;
        public static MeasurementForm Measurement;
        public static ConfirmationForm Confirmation;
        public static GoodsMovementForm Goods;
        public static ShipmentForm Shipment;
        public static SerialPickerForm Serials;
        public static SparePartsForm Spares;
        public static ProductionViewForm Production;

        public static void Register(UdoForm form)
        {
            Forms[form.ObjectCode] = form;
        }

        /// <summary>Ouvre l'objet (code UDO) sur la clé donnée (DocEntry ou Code).</summary>
        public static void Open(string objectCode, string key)
        {
            // Ordre de fabrication SAP : suivi de maintenance de l'OF
            if (objectCode == Services.ReportService.ProdObject)
            {
                if (int.TryParse(key, out int of))
                    Production.Show(of);
                return;
            }
            if (Forms.TryGetValue(objectCode, out UdoForm form))
            {
                form.OpenKey(key);
                return;
            }
            // Objets de paramétrage : fenêtre par défaut SAP
            App.OpenForm(BoFormObjectEnum.fo_UserDefinedObject, objectCode, key ?? "");
        }

        public static void Show(string objectCode)
        {
            if (Forms.TryGetValue(objectCode, out UdoForm form))
                form.Show();
            else
                OpenDefaultForm(objectCode);
        }

        /// <summary>Fenêtre par défaut SAP d'un objet de paramétrage (grille éditable).</summary>
        public static void OpenDefaultForm(string objectCode)
        {
            try
            {
                App.OpenForm(BoFormObjectEnum.fo_UserDefinedObject, objectCode, "");
            }
            catch (Exception ex)
            {
                // Repli : menu Outils → Fenêtres par défaut
                Program.Log("OpenForm " + objectCode + " : " + ex.Message);
                App.ActivateMenuItem(objectCode);
            }
        }
    }
}
