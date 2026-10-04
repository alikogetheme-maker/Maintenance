using System;
using SAPbouiCOM;

namespace MaintenanceAddon.Core
{
    /// <summary>
    /// Écran simple (sans objet UDO) : popups de saisie, rapports, paramètres.
    /// Une seule fenêtre par type ; abonnement unique aux événements.
    /// </summary>
    internal abstract class SimpleForm
    {
        protected readonly Application App;
        protected Form F;
        protected Ui U;

        protected SimpleForm(Application app)
        {
            App = app;
            EventHub.RegisterForm(app, FormType, App_ItemEvent);
        }

        protected abstract string FormType { get; }
        protected abstract string Title { get; }
        protected abstract int FormWidth { get; }
        protected abstract int FormHeight { get; }
        protected abstract void Build();

        /// <summary>Événements après action (boutons, combos, CFL...).</summary>
        protected virtual void OnEvent(ItemEvent e) { }

        /// <summary>Événements avant action ; false bloque l'action SAP.</summary>
        protected virtual bool OnBefore(ItemEvent e) { return true; }

        public bool IsOpen => F != null;

        /// <summary>Crée la fenêtre si besoin ; renvoie false si elle était déjà ouverte.</summary>
        protected bool Open(bool modalPosition = false)
        {
            if (F != null)
            {
                F.Select();
                return false;
            }

            FormCreationParams p = (FormCreationParams)App.CreateObject(BoCreatableObjectType.cot_FormCreationParams);
            p.FormType = FormType;
            p.UniqueID = FormType;
            p.BorderStyle = BoFormBorderStyle.fbs_Fixed;

            F = App.Forms.AddEx(p);
            F.Freeze(true);
            try
            {
                F.Title = Title;
                F.Width = FormWidth;
                F.Height = FormHeight;
                if (modalPosition)
                {
                    F.Left = 300;
                    F.Top = 120;
                }
                U = new Ui(App, F, "");
                Build();
            }
            catch
            {
                Form broken = F;
                F = null;
                try { broken.Close(); } catch { }
                throw;
            }
            finally
            {
                if (F != null)
                    F.Freeze(false);
            }
            F.Visible = true;
            return true;
        }

        public void Close()
        {
            if (F != null)
            {
                try { F.Close(); } catch { }
            }
        }

        protected string Uds(string id)
        {
            return F.DataSources.UserDataSources.Item(id).ValueEx;
        }

        protected void SetUds(string id, string value)
        {
            F.DataSources.UserDataSources.Item(id).ValueEx = value ?? "";
        }

        protected void AddUds(string id, BoDataType type, int length = 0)
        {
            if (length > 0)
                F.DataSources.UserDataSources.Add(id, type, length);
            else
                F.DataSources.UserDataSources.Add(id, type);
        }

        protected void Msg(string text, BoStatusBarMessageType type = BoStatusBarMessageType.smt_Success)
        {
            App.StatusBar.SetText(text, BoMessageTime.bmt_Medium, type);
        }

        protected bool Confirm(string question)
        {
            return Program.Message(App, question, true) == 1;
        }

        protected static string DateValue(DateTime date)
        {
            return date.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Valeur choisie dans une liste de choix (colonne demandée de la 1re ligne), ou null.</summary>
        protected static string Chosen(ItemEvent e, string column)
        {
            IChooseFromListEvent cfe = (IChooseFromListEvent)e;
            DataTable sel = cfe.SelectedObjects;
            if (sel == null || sel.Rows.Count == 0)
                return null;
            return Convert.ToString(sel.GetValue(column, 0), System.Globalization.CultureInfo.InvariantCulture);
        }

        private void App_ItemEvent(string formUID, ref ItemEvent pVal, out bool bubbleEvent)
        {
            bubbleEvent = true;
            if (formUID != FormType)
                return;

            if (pVal.EventType == BoEventTypes.et_FORM_UNLOAD)
            {
                if (!pVal.BeforeAction)
                    F = null;
                return;
            }
            if (F == null)
                return;

            try
            {
                if (pVal.BeforeAction)
                    bubbleEvent = OnBefore(pVal);
                else
                    OnEvent(pVal);
            }
            catch (Exception ex)
            {
                Program.Log(FormType + " : " + ex);
                Program.Message(App, ex.Message);
                if (pVal.BeforeAction)
                    bubbleEvent = false;
            }
        }
    }
}
