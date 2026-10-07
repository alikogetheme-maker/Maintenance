using System;
using SAPbouiCOM;
using MaintenanceAddon.Core;
using MaintenanceAddon.Forms;
using MaintenanceAddon.Setup;

namespace MaintenanceAddon
{
    internal static class Program
    {
        // NB : pas de "using System.Windows.Forms;" ici, ce namespace définit
        // aussi un type "Application" qui entrerait en conflit avec SAPbouiCOM.

        private static Application _app;
        private static ListForm _lists;
        private static SchedulingForm _scheduling;
        private static SettingsForm _settings;

        /// <summary>Journal de démarrage : %TEMP%\MaintenanceAddon.log</summary>
        internal static readonly string LogPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MaintenanceAddon.log");

        /// <summary>
        /// Chaîne de connexion de développement fournie par SAP : permet de
        /// lancer l'add-on depuis Visual Studio (F5) sur le client SAP B1 déjà ouvert.
        /// </summary>
        private const string DevConnectionString = "0030002C0030002C00530041005000420044005F00440061007400650076002C0050004C006F006D0056004900490056";

        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length == 0 && System.Diagnostics.Debugger.IsAttached)
                args = new[] { DevConnectionString };

            // Main ne référence aucun type SAP : une DLL manquante est ainsi
            // interceptée (et journalisée) au lieu de faire planter le processus.
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log("Exception non gérée : " + e.ExceptionObject);
            Log("Démarrage " + typeof(Program).Assembly.Location + " (" + (Environment.Is64BitProcess ? "64" : "32") + " bits), " + args.Length + " argument(s)");

            if (args.Length == 0)
            {
                System.Windows.Forms.MessageBox.Show(
                    "Cet exécutable doit être lancé par SAP Business One (Add-On Administration), pas directement.",
                    "MaintenanceAddon");
                return;
            }

            try
            {
                Start(args[0]);
            }
            catch (Exception ex)
            {
                Log("Échec du démarrage : " + ex);
                System.Windows.Forms.MessageBox.Show("Erreur au démarrage de l'add-on Maintenance : " + ex.Message + "\n\nDétail : " + LogPath, "MaintenanceAddon");
                return;
            }

            Log("Add-on prêt");
            System.Windows.Forms.Application.Run();
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Start(string connectionString)
        {
            _app = SboApplication.Connect(connectionString);
            Log("UI API connectée");
            DiCompany.Connect(_app);
            Log("DI API connectée");

            MetadataSetup.Progress = m => _app.StatusBar.SetText(m, BoMessageTime.bmt_Short, BoStatusBarMessageType.smt_Warning);
            bool created = MetadataSetup.EnsureSchema();
            Log("Tables, champs et objets vérifiés" + (created ? " (créations effectuées)" : ""));

            SboApplication.CreateMenus(_app);
            InitForms(_app);
            _app.AppEvent += App_AppEvent;

            if (created)
                Program.Message(_app, "L'add-on Maintenance a créé ses tables et objets dans la société.\n\n" +
                                "Les autres utilisateurs connectés doivent se reconnecter.\n" +
                                "Complétez ensuite Maintenance → Paramètres (comptes de charges).");
            _app.StatusBar.SetText("Add-on Maintenance démarré.", BoMessageTime.bmt_Short, BoStatusBarMessageType.smt_Success);

            // Alertes du jour (une fois par jour, au premier démarrage) : ne bloque jamais l'add-on
            try
            {
                int message = Services.AlertService.SendDaily(DateTime.Today, false);
                if (message > 0)
                    Log("Alertes du jour envoyées (message " + message + ")");
            }
            catch (Exception ex)
            {
                Log("Alertes du jour : " + ex.Message);
            }
        }

        /// <summary>Crée les écrans et branche les menus (aussi utilisé par le banc de test des écrans).</summary>
        internal static void InitForms(Application app)
        {
            _app = app;
            Navigator.App = app;
            Navigator.Register(Navigator.FuncLoc = new FuncLocForm(app));
            Navigator.Register(Navigator.Equipment = new EquipmentForm(app));
            Navigator.Register(Navigator.TaskList = new TaskListForm(app));
            Navigator.Register(Navigator.Plan = new PlanForm(app));
            Navigator.Register(Navigator.Contract = new ContractForm(app));
            Navigator.Shipment = new ShipmentForm(app);
            Navigator.Register(Navigator.Notification = new NotificationForm(app));
            Navigator.Register(Navigator.Order = new OrderForm(app));
            Navigator.Confirmation = new ConfirmationForm(app);
            Navigator.Goods = new GoodsMovementForm(app);
            Navigator.Measurement = new MeasurementForm(app);
            Navigator.Serials = new SerialPickerForm(app);
            Navigator.Spares = new SparePartsForm(app);
            Navigator.Scheduling = _scheduling = new SchedulingForm(app);
            Navigator.Lists = _lists = new ListForm(app);
            Navigator.Settings = _settings = new SettingsForm(app);
            EventHub.RegisterMenu(app, App_MenuEvent);
        }

        /// <summary>
        /// Banc de test des écrans : intercepte les boîtes de message (réponse
        /// automatique 1 = Oui / OK, 2 = Non) au lieu de bloquer l'automate.
        /// </summary>
        internal static Func<string, int> MessageHook;

        /// <summary>Boîte de message SAP (OK, ou Oui / Non) ; renvoie 1 pour OK / Oui.</summary>
        internal static int Message(Application app, string text, bool yesNo = false)
        {
            if (MessageHook != null)
                return MessageHook(text);
            return yesNo ? app.MessageBox(text, 2, "Oui", "Non") : app.MessageBox(text);
        }

        /// <summary>Banc de test des écrans : remplace la fenêtre de choix de fichier.</summary>
        internal static Func<string, string> PickFileHook;

        /// <summary>Banc de test des écrans : remplace l'ouverture d'un fichier (navigateur, lecteur PDF...).</summary>
        internal static Action<string> OpenFileHook;

        /// <summary>Fenêtre Windows de choix d'un fichier ; null si l'utilisateur annule.</summary>
        internal static string PickFile(string title)
        {
            if (PickFileHook != null)
                return PickFileHook(title);
            string result = null;
            // La boîte de dialogue exige un thread STA, au premier plan devant le client SAP
            var t = new System.Threading.Thread(() =>
            {
                using (var owner = new System.Windows.Forms.Form { TopMost = true, ShowInTaskbar = false, Width = 0, Height = 0 })
                using (var dlg = new System.Windows.Forms.OpenFileDialog { Title = title, Filter = "Tous les fichiers (*.*)|*.*", CheckFileExists = true })
                {
                    owner.Show();
                    owner.Activate();
                    if (dlg.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK)
                        result = dlg.FileName;
                }
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            t.Join();
            return result;
        }

        /// <summary>Ouvre un fichier avec le programme associé de Windows.</summary>
        internal static void OpenFile(string path)
        {
            if (OpenFileHook != null)
            {
                OpenFileHook(path);
                return;
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }

        internal static void Log(string message)
        {
            try
            {
                System.IO.File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch
            {
                // le journal ne doit jamais empêcher l'add-on de fonctionner
            }
        }

        private static void App_MenuEvent(ref MenuEvent pVal, out bool bubbleEvent)
        {
            bubbleEvent = true;
            if (pVal.BeforeAction || !pVal.MenuUID.StartsWith("MNTM_"))
                return;

            try
            {
                switch (pVal.MenuUID)
                {
                    case FormIds.MenuFuncLoc: Navigator.Show(Obj.FuncLoc); break;
                    case FormIds.MenuEquip: Navigator.Show(Obj.Equip); break;
                    case FormIds.MenuTaskList: Navigator.Show(Obj.TaskList); break;
                    case FormIds.MenuWorkCtr: Navigator.OpenDefaultForm(Obj.WorkCtr); break;
                    case FormIds.MenuCatalog: Navigator.OpenDefaultForm(Obj.Catalog); break;
                    case FormIds.MenuEqCat: Navigator.OpenDefaultForm(Obj.EqCat); break;
                    case FormIds.MenuContract: Navigator.Show(Obj.Contract); break;
                    case FormIds.MenuShip: Navigator.Shipment.Show(null, 0, null); break;
                    case FormIds.MenuNotif: Navigator.Show(Obj.Notif); break;
                    case FormIds.MenuOrder: Navigator.Show(Obj.Order); break;
                    case FormIds.MenuMeasure: Navigator.Measurement.Show(null, null); break;
                    case FormIds.MenuPlan: Navigator.Show(Obj.Plan); break;
                    case FormIds.MenuSched: _scheduling.Show(); break;
                    case FormIds.MenuSetup: _settings.Show(); break;
                    default:
                        if (pVal.MenuUID.StartsWith(FormIds.MenuReportPrefix))
                            _lists.Show(pVal.MenuUID.Substring(FormIds.MenuReportPrefix.Length), null);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log("Menu " + pVal.MenuUID + " : " + ex);
                Program.Message(_app, ex.Message);
            }
        }

        private static void App_AppEvent(BoAppEventTypes eventType)
        {
            if (eventType == BoAppEventTypes.aet_ShutDown ||
                eventType == BoAppEventTypes.aet_CompanyChanged ||
                eventType == BoAppEventTypes.aet_ServerTerminition)
            {
                try
                {
                    if (_app.Menus.Exists(FormIds.MenuRoot))
                        _app.Menus.RemoveEx(FormIds.MenuRoot);
                }
                catch
                {
                    // le client est peut-être déjà fermé
                }
                DiCompany.Disconnect();
                System.Windows.Forms.Application.Exit();
            }
        }
    }
}
