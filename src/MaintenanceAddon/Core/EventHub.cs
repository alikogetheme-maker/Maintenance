using System.Collections.Generic;
using SAPbouiCOM;

namespace MaintenanceAddon.Core
{
    internal delegate void ItemHandler(string formUID, ref ItemEvent pVal, out bool bubbleEvent);
    internal delegate void DataHandler(ref BusinessObjectInfo info, out bool bubbleEvent);
    internal delegate void MenuHandler(ref MenuEvent pVal, out bool bubbleEvent);

    /// <summary>
    /// Abonnement UNIQUE aux événements SAP, aiguillés vers l'écran concerné.
    /// Avec plusieurs abonnés .NET sur un même événement COM, la valeur de
    /// bubbleEvent renvoyée à SAP est celle du DERNIER abonné : un refus
    /// d'enregistrement d'un écran était écrasé par les autres (document vide créé).
    /// </summary>
    internal static class EventHub
    {
        private static readonly Dictionary<string, ItemHandler> Items = new Dictionary<string, ItemHandler>();
        private static readonly Dictionary<string, DataHandler> Data = new Dictionary<string, DataHandler>();
        private static readonly List<MenuHandler> Menus = new List<MenuHandler>();
        // Écrans standard de SAP (identifiant variable) : aiguillage par type d'écran
        private static readonly Dictionary<string, ItemHandler> TypeItems = new Dictionary<string, ItemHandler>();
        private static Application _app;

        public static void RegisterForm(Application app, string formType, ItemHandler item, DataHandler data = null)
        {
            Attach(app);
            Items[formType] = item;
            if (data != null)
                Data[formType] = data;
        }

        /// <summary>Événements d'un écran standard SAP, quel que soit son identifiant (ex. "65211" ordre de fabrication).</summary>
        public static void RegisterFormType(Application app, string formTypeEx, ItemHandler item)
        {
            Attach(app);
            TypeItems[formTypeEx] = item;
        }

        public static void RegisterMenu(Application app, MenuHandler handler)
        {
            Attach(app);
            Menus.Add(handler);
        }

        private static void Attach(Application app)
        {
            if (_app != null)
                return;
            _app = app;
            app.ItemEvent += OnItem;
            app.FormDataEvent += OnData;
            app.MenuEvent += OnMenu;
        }

        private static void OnItem(string formUID, ref ItemEvent pVal, out bool bubbleEvent)
        {
            bubbleEvent = true;
            if (Items.TryGetValue(formUID, out ItemHandler h))
                h(formUID, ref pVal, out bubbleEvent);
            else if (TypeItems.Count > 0 && TypeItems.TryGetValue(pVal.FormTypeEx, out ItemHandler t))
                t(formUID, ref pVal, out bubbleEvent);
        }

        private static void OnData(ref BusinessObjectInfo info, out bool bubbleEvent)
        {
            bubbleEvent = true;
            if (Data.TryGetValue(info.FormUID, out DataHandler h))
                h(ref info, out bubbleEvent);
        }

        private static void OnMenu(ref MenuEvent pVal, out bool bubbleEvent)
        {
            bubbleEvent = true;
            foreach (MenuHandler h in Menus)
            {
                h(ref pVal, out bool b);
                if (!b)
                    bubbleEvent = false;
            }
        }
    }
}
