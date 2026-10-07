using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MaintenanceAddon;
using MaintenanceAddon.Core;
using MaintenanceAddon.Forms;
using MaintenanceAddon.Models;
using MaintenanceAddon.Services;
using MaintenanceAddon.Setup;
using UI = SAPbouiCOM;
using DI = SAPbobsCOM;

namespace MntUiTest
{
    /// <summary>
    /// Banc de test des ÉCRANS : se connecte au client SAP B1 ouvert (comme
    /// l'add-on lancé en F5), crée le menu Maintenance, puis clique les menus,
    /// saisit, enregistre et appuie sur les boutons comme un utilisateur. Les
    /// boîtes de message sont interceptées (réponse Oui) ; les erreurs de la
    /// barre d'état et du journal de l'add-on sont relevées à chaque étape.
    /// Données créées (suffixe = heure) : poste technique UIF*, équipement UIE*,
    /// gamme UIG* (supprimée), avis et ordres (ordres clôturés ou annulés à la fin).
    /// </summary>
    internal static class Program
    {
        private const string DevConnectionString = "0030002C0030002C00530041005000420044005F00440061007400650076002C0050004C006F006D0056004900490056";
        private static readonly string S = DateTime.Now.ToString("MMddHHmm", CultureInfo.InvariantCulture);

        private static UI.Application _app;
        private static int _ok, _ko;
        private static readonly List<string> Messages = new List<string>();
        private static readonly List<string> StatusErrors = new List<string>();
        private static readonly List<string> StatusAll = new List<string>();
        private static readonly List<int> Orders = new List<int>();
        private static readonly List<string> Opened = new List<string>();
        private static readonly List<string> ShareFiles = new List<string>();
        private static int _direct;

        private static void Check(bool cond, string label)
        {
            if (cond) { _ok++; Console.WriteLine("  OK  " + label); }
            else { _ko++; Console.WriteLine("  KO  " + label); }
        }

        /// <summary>Étape : relève boîtes de message, erreurs de barre d'état et lignes ajoutées au journal.</summary>
        private static void Step(string title, Action a, bool errorsExpected = false)
        {
            Console.WriteLine("== " + title);
            Messages.Clear();
            StatusErrors.Clear();
            StatusAll.Clear();
            long logPos = LogLength();
            try
            {
                a();
                Pump();
            }
            catch (Exception ex)
            {
                _ko++;
                Console.WriteLine("  KO  EXCEPTION : " + ex.GetType().Name + " : " + ex.Message);
                Console.WriteLine("      " + ex.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("MntUiTest") || l.Contains("MaintenanceAddon"))?.Trim());
                DumpForms();
            }
            foreach (string m in Messages)
                Console.WriteLine("      [message] " + m.Replace("\n", " / "));
            foreach (string s in StatusAll)
                Console.WriteLine("      [barre d'état] " + s);
            string log = LogSince(logPos);
            if (!errorsExpected)
            {
                var problems = Messages.Where(m => !m.Contains("?") && !m.Contains("créé")).ToList();
                problems.AddRange(StatusErrors);
                if (log.Trim() != "")
                    problems.Add("journal : " + log.Trim());
                if (problems.Count > 0)
                {
                    _ko++;
                    Console.WriteLine("  KO  erreurs pendant l'étape :");
                    foreach (string p in problems)
                        Console.WriteLine("      " + p.Replace("\r\n", "\n").Replace("\n", "\n      "));
                    DumpForms();
                }
            }
        }

        private static void DumpForms()
        {
            try
            {
                for (int i = 0; i < _app.Forms.Count; i++)
                {
                    UI.Form f = _app.Forms.Item(i);
                    if (f.Visible)
                        Console.WriteLine("      [fenêtre] " + f.UniqueID + " type " + f.TypeEx + " « " + f.Title + " » mode " + f.Mode);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("      (liste des fenêtres : " + ex.Message + ")");
            }
        }

        private static long LogLength()
        {
            try { return new FileInfo(MaintenanceAddon.Program.LogPath).Length; } catch { return 0; }
        }

        private static string LogSince(long pos)
        {
            try
            {
                using (var fs = new FileStream(MaintenanceAddon.Program.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Length <= pos) return "";
                    fs.Seek(pos, SeekOrigin.Begin);
                    return new StreamReader(fs).ReadToEnd();
                }
            }
            catch { return ""; }
        }

        /// <summary>Laisse SAP traiter ses événements (UI API asynchrone).</summary>
        private static void Pump(int ms = 400)
        {
            var end = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < end)
            {
                System.Windows.Forms.Application.DoEvents();
                System.Threading.Thread.Sleep(20);
            }
        }

        // ---------------------------------------------------------------------
        // Outils de pilotage
        // ---------------------------------------------------------------------

        private static UI.Form Form(string uid)
        {
            return _app.Forms.Item(uid);
        }

        private static bool IsOpen(string uid)
        {
            for (int i = 0; i < _app.Forms.Count; i++)
                if (_app.Forms.Item(i).UniqueID == uid)
                    return true;
            return false;
        }

        private static void Menu(string uid, string expectedForm = null)
        {
            _app.ActivateMenuItem(uid);
            Pump();
            for (int i = 0; expectedForm != null && i < 15 && !IsOpen(expectedForm); i++)
                Pump(200);
        }

        private static void Set(UI.Form f, string item, string value)
        {
            ((UI.EditText)f.Items.Item(item).Specific).Value = value;
            Pump(150);
        }

        private static string Get(UI.Form f, string item)
        {
            return ((UI.EditText)f.Items.Item(item).Specific).Value.Trim();
        }

        private static string ComboValue(UI.Form f, string item)
        {
            return ((UI.ComboBox)f.Items.Item(item).Specific).Selected?.Value ?? "";
        }

        private static void Click(UI.Form f, string item)
        {
            f.Items.Item(item).Click(UI.BoCellClickType.ct_Regular);
            Pump();
        }

        private static void Cell(UI.Form f, string matrix, string col, int row, string value)
        {
            var m = (UI.Matrix)f.Items.Item(matrix).Specific;
            ((UI.EditText)m.Columns.Item(col).Cells.Item(row).Specific).Value = value;
            Pump(150);
        }

        private static string Uds(UI.Form f, string id)
        {
            return f.DataSources.UserDataSources.Item(id).ValueEx;
        }

        private static void CloseForm(string uid)
        {
            try
            {
                if (IsOpen(uid))
                {
                    UI.Form f = Form(uid);
                    // Évite la question « enregistrer les modifications ? » de SAP à la fermeture
                    if (f.Mode == UI.BoFormMode.fm_UPDATE_MODE || f.Mode == UI.BoFormMode.fm_ADD_MODE)
                        f.Mode = UI.BoFormMode.fm_FIND_MODE;
                    f.Close();
                    Pump(200);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("      (fermeture " + uid + " : " + ex.Message + ")");
            }
        }

        private static void CloseAll()
        {
            foreach (string uid in new[] { FormIds.SerialForm, FormIds.SparePartsForm, FormIds.ConfForm, FormIds.GoodsForm, FormIds.ShipForm, FormIds.MeasureForm, FormIds.OrderForm, FormIds.NotifForm,
                                           FormIds.EquipForm, FormIds.FuncLocForm, FormIds.TaskListForm, FormIds.PlanForm, FormIds.ContractForm,
                                           FormIds.SchedForm, FormIds.ListForm, FormIds.SetupForm })
                CloseForm(uid);
        }

        /// <summary>Menu « Créer » (1282) de SAP sur la fenêtre, comme l'utilisateur.</summary>
        private static void NewRecord(UI.Form f)
        {
            if (f.Mode == UI.BoFormMode.fm_ADD_MODE)
                return; // menu « Créer » grisé : déjà en création
            f.Select();
            Pump(150);
            _app.ActivateMenuItem("1282");
            Pump();
        }

        private static int MaxDoc(string table)
        {
            return (int)Sql.ScalarDbl("SELECT ISNULL(MAX(\"DocEntry\"), 0) FROM \"@" + table + "\"");
        }

        // ---------------------------------------------------------------------

        [STAThread]
        private static int Main()
        {
            _app = SboApplication.Connect(DevConnectionString);
            DiCompany.Connect(_app);
            Console.WriteLine("Connecté au client SAP : " + DiCompany.Instance.CompanyDB + " (" + DiCompany.Instance.UserName + "), suffixe " + S);
            if (DiCompany.Instance.CompanyDB != "TST_TST")
            {
                Console.WriteLine("Ce banc ne s'exécute que sur TST_TST.");
                return 2;
            }
            string other = System.Diagnostics.Process.GetProcesses().Select(p => p.ProcessName)
                .FirstOrDefault(n => n.EndsWith("UiTest", StringComparison.OrdinalIgnoreCase) && n != "MntUiTest");
            if (other != null)
            {
                Console.WriteLine("Un autre banc d'écrans (" + other + ") pilote déjà ce client SAP : relancez quand il est terminé.");
                return 3;
            }
            MetadataSetup.EnsureSchema();

            MaintenanceAddon.Program.MessageHook = text => { Messages.Add(text); return 1; };
            MaintenanceAddon.Program.OpenFileHook = path => Opened.Add(path);
            MaintenanceAddon.Program.PickFileHook = title =>
            {
                string f = Path.Combine(Path.GetTempPath(), "MntUiTest", "notice-" + S + ".txt");
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllText(f, "Notice de test " + S);
                return f;
            };
            _app.StatusBarEvent += (string text, UI.BoStatusBarMessageType type) =>
            {
                StatusAll.Add((type == UI.BoStatusBarMessageType.smt_Error ? "ERREUR " : "") + text);
                if (type == UI.BoStatusBarMessageType.smt_Error)
                    StatusErrors.Add(text);
            };

            SboApplication.CreateMenus(_app);
            MaintenanceAddon.Program.InitForms(_app);
            Pump();

            try
            {
                Run();
            }
            finally
            {
                Step("Nettoyage", Cleanup);
                try { if (_app.Menus.Exists(FormIds.MenuRoot)) _app.Menus.RemoveEx(FormIds.MenuRoot); } catch { }
                Console.WriteLine();
                Console.WriteLine("RÉSULTAT : " + _ok + " OK, " + _ko + " KO");
                DiCompany.Disconnect();
            }
            return _ko == 0 ? 0 : 1;
        }

        private static void Run()
        {
            string fl = "UIF" + S, eq = "UIE" + S, tsk = "UIG" + S;
            string ocr = Sql.ScalarStr("SELECT TOP 1 \"PrcCode\" FROM \"OPRC\" WHERE \"DimCode\" = " + SettingsService.Load().Dimension + " AND \"Active\" = 'Y' ORDER BY \"PrcCode\"");

            // ---- Tous les menus s'ouvrent sans erreur ---------------------------
            var menus = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(FormIds.MenuFuncLoc, FormIds.FuncLocForm),
                new KeyValuePair<string, string>(FormIds.MenuEquip, FormIds.EquipForm),
                new KeyValuePair<string, string>(FormIds.MenuTaskList, FormIds.TaskListForm),
                new KeyValuePair<string, string>(FormIds.MenuContract, FormIds.ContractForm),
                new KeyValuePair<string, string>(FormIds.MenuShip, FormIds.ShipForm),
                new KeyValuePair<string, string>(FormIds.MenuNotif, FormIds.NotifForm),
                new KeyValuePair<string, string>(FormIds.MenuOrder, FormIds.OrderForm),
                new KeyValuePair<string, string>(FormIds.MenuMeasure, FormIds.MeasureForm),
                new KeyValuePair<string, string>(FormIds.MenuPlan, FormIds.PlanForm),
                new KeyValuePair<string, string>(FormIds.MenuSched, FormIds.SchedForm),
                new KeyValuePair<string, string>(FormIds.MenuSetup, FormIds.SetupForm),
                new KeyValuePair<string, string>(FormIds.MenuWorkCtr, null),
                new KeyValuePair<string, string>(FormIds.MenuCatalog, null),
                new KeyValuePair<string, string>(FormIds.MenuEqCat, null)
            };
            foreach (ReportView v in ReportService.Views)
                menus.Add(new KeyValuePair<string, string>(FormIds.MenuReportPrefix + v.Code, FormIds.ListForm));
            foreach (var m in menus)
            {
                Step("Menu " + m.Key, () =>
                {
                    string before = "";
                    try { before = _app.Forms.ActiveForm.UniqueID; } catch { }
                    Menu(m.Key, m.Value);
                    if (m.Value != null)
                        Check(IsOpen(m.Value), "Écran " + m.Value + " ouvert");
                    else
                    {
                        UI.Form f = _app.Forms.ActiveForm;
                        Check(f.UniqueID != before && f.TypeEx.Contains("MNT_"), "Fenêtre par défaut ouverte (" + f.TypeEx + ")");
                        f.Close();
                    }
                    if (m.Value != null && m.Value != FormIds.ListForm)
                        CloseForm(m.Value);
                });
            }
            CloseAll();

            // ---- Poste technique ------------------------------------------------
            Step("Poste technique : création à l'écran", () =>
            {
                Menu(FormIds.MenuFuncLoc, FormIds.FuncLocForm);
                UI.Form f = Form(FormIds.FuncLocForm);
                Check(f.Mode == UI.BoFormMode.fm_ADD_MODE, "Mode création");
                Set(f, UdoForm.KeyItem, fl);
                Set(f, "eName", "Atelier UI " + S);
                Set(f, "eLoc", "Zone industrielle");
                if (ocr != "")
                    Set(f, "eOcr", ocr);
                Set(f, "eWhs", "ABJ");
                Click(f, "1");
                Row r = Sql.First("SELECT * FROM " + Db.T(Db.FuncLoc) + " WHERE \"Code\" = " + Sql.Q(fl));
                Check(r != null && r.Str("U_Location") == "Zone industrielle" && r.Str("U_Whs") == "ABJ" && r.Str("U_OcrCode") == ocr, "Poste enregistré en base (centre de coûts " + ocr + ")");
                Check(r != null && r.Str("U_Active") == "Y", "Valeur par défaut « Actif » enregistrée");
                Check(f.Mode == UI.BoFormMode.fm_ADD_MODE, "Retour en création après « Créer » (standard SAP)");
            });

            Step("Poste technique : refus sans désignation (rien enregistré)", () =>
            {
                UI.Form f = Form(FormIds.FuncLocForm);
                if (f.Mode != UI.BoFormMode.fm_ADD_MODE)
                    NewRecord(f);
                Set(f, UdoForm.KeyItem, "UIX" + S);
                Set(f, "eName", "");
                Click(f, "1");
                Check(!Sql.Exists("SELECT 1 FROM " + Db.T(Db.FuncLoc) + " WHERE \"Code\" = " + Sql.Q("UIX" + S)), "Aucun poste créé malgré le refus");
                Check(StatusErrors.Any(s => s.Contains("désignation")), "Refus affiché dans la barre d'état");
                Check(f.Mode == UI.BoFormMode.fm_ADD_MODE, "Toujours en création");
            }, errorsExpected: true);

            Step("Poste technique : recherche, modification, nouvel équipement", () =>
            {
                UI.Form f = Form(FormIds.FuncLocForm);
                f.Mode = UI.BoFormMode.fm_ADD_MODE;
                Navigator.Open(Obj.FuncLoc, fl);
                Pump();
                Check(f.Mode == UI.BoFormMode.fm_OK_MODE && Get(f, UdoForm.KeyItem) == fl, "Poste rouvert");
                Set(f, "eLoc", "Zone industrielle, bât. B");
                Check(f.Mode == UI.BoFormMode.fm_UPDATE_MODE, "Passage en mise à jour");
                Click(f, "1");
                Check(Sql.ScalarStr("SELECT \"U_Location\" FROM " + Db.T(Db.FuncLoc) + " WHERE \"Code\" = " + Sql.Q(fl)) == "Zone industrielle, bât. B", "Modification enregistrée");
                Check(f.Mode == UI.BoFormMode.fm_OK_MODE, "Mode OK après mise à jour");
                Click(f, "bNewEq");
                Check(IsOpen(FormIds.EquipForm), "Fiche équipement ouverte");
                UI.Form e = Form(FormIds.EquipForm);
                Check(e.Mode == UI.BoFormMode.fm_ADD_MODE && Get(e, "eFl") == fl, "Équipement en création sur le poste " + fl);
            });

            // ---- Équipement -----------------------------------------------------
            Step("Équipement : création avec un point de mesure", () =>
            {
                UI.Form f = Form(FormIds.EquipForm);
                Set(f, UdoForm.KeyItem, eq);
                Set(f, "eName", "Compresseur UI " + S);
                Set(f, "eWc", "MECA");
                Click(f, "fPts");
                Click(f, "bPtAdd");
                Cell(f, "mPts", "cPoint", 1, "H");
                Cell(f, "mPts", "cDescr", 1, "Heures de marche");
                Cell(f, "mPts", "cUnit", 1, "h");
                Click(f, "fGen");
                Click(f, "1");
                Row r = Sql.First("SELECT * FROM " + Db.T(Db.Equip) + " WHERE \"Code\" = " + Sql.Q(eq));
                Check(r != null && r.Str("U_FuncLoc") == fl && r.Str("U_Status") == EquipStatus.Active, "Équipement enregistré sur le poste, statut actif");
                Check(Sql.Exists("SELECT 1 FROM " + Db.T(Db.EquipPts) + " WHERE \"Code\" = " + Sql.Q(eq) + " AND \"U_Point\" = 'H' AND \"U_Counter\" = 'N' AND \"U_Unit\" = 'h'"),
                      "Point de mesure enregistré (avec sa valeur par défaut « compteur = N »)");
                Check(f.Mode == UI.BoFormMode.fm_ADD_MODE, "Retour en création");
                Check(((UI.Matrix)f.Items.Item("mPts").Specific).RowCount == 0, "Lignes vidées pour la saisie suivante");
                Click(f, "eName");
                Check(ComboValue(f, "cStatus") == EquipStatus.Active, "Statut par défaut remis pour la saisie suivante");
            });

            Step("Équipement : recherche, onglets, création d'un avis", () =>
            {
                UI.Form f = Form(FormIds.EquipForm);
                f.Mode = UI.BoFormMode.fm_ADD_MODE;
                Navigator.Open(Obj.Equip, eq);
                Pump();
                Check(f.Mode == UI.BoFormMode.fm_OK_MODE && Get(f, UdoForm.KeyItem) == eq, "Équipement rouvert");
                Check(Uds(f, "udFlNm").Contains("Atelier UI"), "Nom du poste affiché (« " + Uds(f, "udFlNm") + " »)");
                foreach (string folder in new[] { "fTech", "fPts", "fHist", "fRem", "fGen" })
                    Click(f, folder);
                Check(f.Mode == UI.BoFormMode.fm_OK_MODE, "Onglets parcourus sans modification");
                Click(f, "bNotif");
                Check(IsOpen(FormIds.NotifForm), "Avis ouvert");
                UI.Form n = Form(FormIds.NotifForm);
                Check(n.Mode == UI.BoFormMode.fm_ADD_MODE && Get(n, "eEq") == eq && Get(n, "eFl") == fl, "Avis prérempli (équipement et poste)");
            });

            // ---- Avis -----------------------------------------------------------
            int notif = 0;
            Step("Avis : refus sans description courte (aucun document vide)", () =>
            {
                UI.Form f = Form(FormIds.NotifForm);
                int before = MaxDoc(Db.Notif);
                Set(f, "eSubj", "");
                Click(f, "1");
                Check(MaxDoc(Db.Notif) == before, "Aucun avis créé malgré le refus");
                Check(StatusErrors.Any(s => s.Contains("description courte")), "Refus affiché dans la barre d'état");
                Check(f.Mode == UI.BoFormMode.fm_ADD_MODE, "Toujours en création");
            }, errorsExpected: true);

            Step("Avis : création", () =>
            {
                UI.Form f = Form(FormIds.NotifForm);
                Set(f, "eSubj", "Bruit anormal UI " + S);
                Click(f, "1");
                Pump(800);
                notif = (int)Sql.ScalarDbl("SELECT ISNULL(MAX(\"DocEntry\"), 0) FROM " + Db.T(Db.Notif) + " WHERE \"U_Equip\" = " + Sql.Q(eq));
                Row r = notif == 0 ? null : Sql.First("SELECT * FROM " + Db.T(Db.Notif) + " WHERE \"DocEntry\" = " + notif);
                Check(r != null && r.Str("U_Subject") == "Bruit anormal UI " + S && r.Str("U_Status") == NotifStatus.Outstanding && r.Str("U_Type") == NotifTypes.Malfunction,
                      "Avis enregistré (n° interne " + notif + ")");
                Check(f.Mode == UI.BoFormMode.fm_ADD_MODE, "Retour en création après « Créer » (standard SAP)");
                Check(StatusAll.Any(s => s.Contains("créé")), "Message du n° créé");
                Click(f, "eSubj");
                Check(ComboValue(f, "cPrio") == "3" && ComboValue(f, "cType") == NotifTypes.Malfunction, "Valeurs par défaut remises pour la saisie suivante");
            });

            int order = 0;
            Step("Avis : rouvert, création de l'ordre", () =>
            {
                UI.Form f = Form(FormIds.NotifForm);
                f.Mode = UI.BoFormMode.fm_ADD_MODE;
                Navigator.Open(Obj.Notif, notif.ToString(CultureInfo.InvariantCulture));
                Pump();
                Check(f.Mode == UI.BoFormMode.fm_OK_MODE && Get(f, UdoForm.KeyItem) == notif.ToString(CultureInfo.InvariantCulture), "Avis rouvert");
                Check(f.Items.Item("bOrder").Enabled, "Bouton « Créer l'ordre » actif");
                Click(f, "bOrder");
                Pump(800);
                order = (int)Sql.ScalarDbl("SELECT ISNULL(\"U_OrderNo\", 0) FROM " + Db.T(Db.Notif) + " WHERE \"DocEntry\" = " + notif);
                if (order > 0) Orders.Add(order);
                Check(order > 0 && OrderService.Status(order) == OrderStatus.Created, "Ordre " + order + " créé depuis l'avis");
                Check(Sql.ScalarStr("SELECT \"U_Status\" FROM " + Db.T(Db.Notif) + " WHERE \"DocEntry\" = " + notif) == NotifStatus.InProcess, "Avis en cours (NOPR)");
                Check(Get(f, "eOrder") == order.ToString(CultureInfo.InvariantCulture), "Avis rechargé avec le n° d'ordre");
                Check(IsOpen(FormIds.OrderForm) && Get(Form(FormIds.OrderForm), UdoForm.KeyItem) == order.ToString(CultureInfo.InvariantCulture), "Ordre ouvert à l'écran");
            });

            // ---- Ordre ----------------------------------------------------------
            Step("Ordre : ajout d'une opération et mise à jour (recalcul des coûts)", () =>
            {
                UI.Form f = Form(FormIds.OrderForm);
                Check(f.Mode == UI.BoFormMode.fm_OK_MODE, "Ordre en mode OK");
                Click(f, "fOps");
                var m = (UI.Matrix)f.Items.Item("mOps").Specific;
                int rows = m.RowCount;
                Click(f, "bOpAdd");
                Check(m.RowCount == rows + 1, "Ligne d'opération ajoutée (" + m.RowCount + ")");
                Cell(f, "mOps", "cDescr", m.RowCount, "Contrôle UI " + S);
                Cell(f, "mOps", "cHrs", m.RowCount, "2");
                Check(f.Mode == UI.BoFormMode.fm_UPDATE_MODE, "Passage en mise à jour");
                Click(f, "1");
                Pump(800);
                Row op = Sql.First("SELECT * FROM " + Db.T(Db.OrderOps) + " WHERE \"DocEntry\" = " + order + " AND \"U_Descr\" = " + Sql.Q("Contrôle UI " + S));
                Check(op != null && op.Dbl("U_PlanHrs") == 2 && op.Str("U_Done") == "N" && op.Str("U_CtrlKey") == ControlKeys.Internal,
                      "Opération enregistrée (heures, clé INT et « terminée = N » par défaut)");
                double plLab = Sql.ScalarDbl("SELECT \"U_PlLab\" FROM " + Db.T(Db.Order) + " WHERE \"DocEntry\" = " + order);
                Check(plLab > 0, "Coût prévu recalculé en base (" + plLab + ")");
                Check(f.Mode == UI.BoFormMode.fm_OK_MODE, "Mode OK après mise à jour");
                Check(Sql.ParseDouble(Get(f, "ePLab")) == plLab, "Coût prévu à jour à l'écran (" + Get(f, "ePLab") + ")");
            });

            Step("Ordre : refus sans équipement (aucun document vide)", () =>
            {
                UI.Form f = Form(FormIds.OrderForm);
                NewRecord(f);
                int before = MaxDoc(Db.Order);
                Set(f, "eSubj", "Sans équipement");
                Click(f, "1");
                Check(MaxDoc(Db.Order) == before, "Aucun ordre créé malgré le refus");
                Check(StatusErrors.Any(s => s.Contains("équipement")), "Refus affiché dans la barre d'état");
            }, errorsExpected: true);

            Step("Ordre : création directe à l'écran", () =>
            {
                UI.Form f = Form(FormIds.OrderForm);
                NewRecord(f);
                Set(f, "eEq", eq);
                Set(f, "eSubj", "Graissage UI " + S);
                Click(f, "fOps");
                Click(f, "bOpAdd");
                Cell(f, "mOps", "cDescr", 1, "Graissage");
                Cell(f, "mOps", "cHrs", 1, "1");
                Click(f, "1");
                Pump(800);
                int direct = (int)Sql.ScalarDbl("SELECT ISNULL(MAX(\"DocEntry\"), 0) FROM " + Db.T(Db.Order) + " WHERE \"U_Subject\" = " + Sql.Q("Graissage UI " + S));
                if (direct > 0) Orders.Add(direct);
                _direct = direct;
                Row r = direct == 0 ? null : Sql.First("SELECT * FROM " + Db.T(Db.Order) + " WHERE \"DocEntry\" = " + direct);
                Check(r != null && r.Str("U_Equip") == eq && r.Str("U_Status") == OrderStatus.Created && r.Str("U_FuncLoc") == fl, "Ordre enregistré (n° interne " + direct + ")");
                Check(r != null && r.Dbl("U_PlLab") > 0, "Coûts prévus calculés après création");
                Check(Sql.Exists("SELECT 1 FROM " + Db.T(Db.OrderOps) + " WHERE \"DocEntry\" = " + direct + " AND \"U_Descr\" = 'Graissage' AND \"U_Done\" = 'N'"), "Opération enregistrée");
                Check(f.Mode == UI.BoFormMode.fm_ADD_MODE, "Retour en création après « Créer » (standard SAP)");
                Check(StatusAll.Any(s => s.Contains("créé")), "Message du n° créé");
                Check(((UI.Matrix)f.Items.Item("mOps").Specific).RowCount == 0, "Lignes vidées pour la saisie suivante");
                Click(f, "eSubj");
                Check(Get(f, "eStart") != "" && Get(f, "eEnd") != "", "Dates prévues remises");
                Check(ComboValue(f, "cType") == OrderTypes.Corrective && ComboValue(f, "cStatus") == OrderStatus.Created, "Valeurs par défaut remises");
            });

            Step("Ordre : lancement, confirmation de temps", () =>
            {
                UI.Form f = Form(FormIds.OrderForm);
                f.Mode = UI.BoFormMode.fm_ADD_MODE;
                Navigator.Open(Obj.Order, order.ToString(CultureInfo.InvariantCulture));
                Pump();
                Check(f.Mode == UI.BoFormMode.fm_OK_MODE && Get(f, UdoForm.KeyItem) == order.ToString(CultureInfo.InvariantCulture), "Ordre rouvert");
                Click(f, "bRel");
                Check(OrderService.Status(order) == OrderStatus.Released && ComboValue(f, "cStatus") == OrderStatus.Released, "Ordre lancé (REL) et rechargé");
                Click(f, "fOps");
                Click(f, "bConf");
                Check(IsOpen(FormIds.ConfForm), "Écran de confirmation ouvert");
                UI.Form c = Form(FormIds.ConfForm);
                Set(c, "eHrs", "1.5");
                Click(c, "bOk");
                Pump(800);
                Check(Sql.ScalarDbl("SELECT SUM(\"U_Hours\") FROM " + Db.T(Db.Conf) + " WHERE \"U_OrderNo\" = " + order) == 1.5, "Confirmation de 1,5 h enregistrée");
                Check(!IsOpen(FormIds.ConfForm), "Écran de confirmation fermé");
                Check(Sql.ParseDouble(Get(f, "eALab")) > 0, "Coût réel MO affiché sur l'ordre rechargé (" + Get(f, "eALab") + ")");
            });

            Step("Ordre : clôture technique puis clôture", () =>
            {
                UI.Form f = Form(FormIds.OrderForm);
                Click(f, "bTeco");
                Check(OrderService.Status(order) == OrderStatus.TechCompleted, "Ordre clôturé techniquement (TECO)");
                Check(Sql.ScalarStr("SELECT \"U_Status\" FROM " + Db.T(Db.Notif) + " WHERE \"DocEntry\" = " + notif) == NotifStatus.Completed, "Avis terminé (NOCO)");
                Check(f.Items.Item("bClose").Enabled && !f.Items.Item("bRel").Enabled, "Boutons adaptés au statut TECO");
                Click(f, "bClose");
                Check(OrderService.Status(order) == OrderStatus.Closed, "Ordre clôturé (CLSD)");
                f.Mode = UI.BoFormMode.fm_OK_MODE;
                Set(f, "eSubj", "Modif interdite");
                Click(f, "1");
                Check(Sql.ScalarStr("SELECT \"U_Subject\" FROM " + Db.T(Db.Order) + " WHERE \"DocEntry\" = " + order) != "Modif interdite", "Modification d'un ordre clôturé refusée");
                CloseForm(FormIds.OrderForm);
            }, errorsExpected: true);

            // ---- Gamme ----------------------------------------------------------
            Step("Gamme : création avec une opération", () =>
            {
                Menu(FormIds.MenuTaskList, FormIds.TaskListForm);
                UI.Form f = Form(FormIds.TaskListForm);
                Set(f, UdoForm.KeyItem, tsk);
                Set(f, "eName", "Révision UI " + S);
                Click(f, "bOpAdd");
                Cell(f, "mOps", "cDescr", 1, "Contrôle visuel");
                Cell(f, "mOps", "cHrs", 1, "0.5");
                Click(f, "1");
                Check(Sql.Exists("SELECT 1 FROM " + Db.T(Db.TaskOps) + " WHERE \"Code\" = " + Sql.Q(tsk) + " AND \"U_Descr\" = 'Contrôle visuel' AND \"U_OpNo\" = '0010' AND \"U_CtrlKey\" = 'INT'"),
                      "Gamme et opération enregistrées (n° 0010, clé INT)");
                Check(Sql.ScalarStr("SELECT \"U_OrdType\" FROM " + Db.T(Db.TaskList) + " WHERE \"Code\" = " + Sql.Q(tsk)) == OrderTypes.Preventive, "Type d'ordre par défaut PM02");
                CloseForm(FormIds.TaskListForm);
            });

            // ---- Lot 3 : intégration SAP -----------------------------------------
            Step("Équipement : pièces de rechange", () =>
            {
                Menu(FormIds.MenuEquip, FormIds.EquipForm);
                UI.Form f = Form(FormIds.EquipForm);
                f.Mode = UI.BoFormMode.fm_ADD_MODE;
                Navigator.Open(Obj.Equip, eq);
                Pump();
                Check(f.Mode == UI.BoFormMode.fm_OK_MODE, "Équipement rouvert");
                Click(f, "fParts");
                Click(f, "bPaAdd");
                Check(((UI.Matrix)f.Items.Item("mParts").Specific).RowCount == 1, "Ligne de pièce ajoutée");
                Cell(f, "mParts", "cItem", 1, "CAS_HNKB_50");
                Cell(f, "mParts", "cName", 1, "Pièce UI");
                Cell(f, "mParts", "cQty", 1, "3");
                Click(f, "1");
                Pump(600);
                Check(Sql.Exists("SELECT 1 FROM " + Db.T(Db.EquipParts) + " WHERE \"Code\" = " + Sql.Q(eq) + " AND \"U_ItemCode\" = 'CAS_HNKB_50' AND \"U_Qty\" = 3"),
                      "Pièce de rechange enregistrée (quantité 3)");
                Check(f.Mode == UI.BoFormMode.fm_OK_MODE, "Mode OK après mise à jour");
                Click(f, "fTech");
                bool asset = true;
                try { f.Items.Item("eAsset"); } catch { asset = false; }
                Check(!asset, "Module Immobilisations non utilisé : pas de champ « Immobilisation SAP »");
            });

            Step("Équipement : documents joints", () =>
            {
                UI.Form f = Form(FormIds.EquipForm);
                Click(f, "fDocs");
                Click(f, "bAtAdd");
                Pump(600);
                int atc = (int)Sql.ScalarDbl("SELECT ISNULL(\"U_AtcEntry\", 0) FROM " + Db.T(Db.Equip) + " WHERE \"Code\" = " + Sql.Q(eq));
                Check(atc > 0 && AttachmentService.Files(atc).Count == 1, "Document joint (pièce jointe SAP n° " + atc + ")");
                foreach (Row r in AttachmentService.Files(atc))
                    ShareFiles.Add(AttachmentService.FullPath(atc, r.Int("Line")));
                var g = (UI.Grid)f.Items.Item("gAtc").Specific;
                Check(g.Rows.Count == 1 && f.Mode == UI.BoFormMode.fm_OK_MODE, "Grille des documents rechargée");
                g.Rows.SelectedRows.Add(0);
                Click(f, "bAtOpen");
                Check(Opened.Count > 0 && File.Exists(Opened.Last()) && Opened.Last().Contains(eq + "_notice"), "Ouverture du fichier : " + Opened.LastOrDefault());
                Click(f, "bAtDel");
                Pump(600);
                Check(Sql.ScalarDbl("SELECT ISNULL(\"U_AtcEntry\", 0) FROM " + Db.T(Db.Equip) + " WHERE \"Code\" = " + Sql.Q(eq)) == 0, "Document retiré");
                Check(((UI.Grid)f.Items.Item("gAtc").Specific).Rows.Count <= 1 && Ui.IsEmpty(f.DataSources.DataTables.Item("dtAtc")), "Grille vide");
            });

            string ser = "UISN" + S, eqSer = "UIS" + S;
            Step("Équipement : création depuis un n° de série reçu", () =>
            {
                // Réception d'achat d'un climatiseur géré par n° de série (article créé par le banc DI)
                var pdn = (DI.Documents)DiCompany.Instance.GetBusinessObject(DI.BoObjectTypes.oPurchaseDeliveryNotes);
                pdn.CardCode = "F00000001"; pdn.DocDate = DateTime.Today; pdn.DocDueDate = DateTime.Today; pdn.Comments = "Test écrans add-on Maintenance";
                pdn.Lines.ItemCode = "MNTTEST_SER"; pdn.Lines.Quantity = 1; pdn.Lines.WarehouseCode = "ABJ"; pdn.Lines.UnitPrice = 380000;
                pdn.Lines.SerialNumbers.InternalSerialNumber = ser; pdn.Lines.SerialNumbers.WarrantyEnd = DateTime.Today.AddYears(1); pdn.Lines.SerialNumbers.Quantity = 1;
                DiCompany.ThrowIfError(pdn.Add(), "Réception de test");

                UI.Form f = Form(FormIds.EquipForm);
                NewRecord(f);
                Click(f, "fTech");
                Click(f, "bSerial");
                Check(IsOpen(FormIds.SerialForm), "Liste des n° de série reçus ouverte");
                UI.Form p = Form(FormIds.SerialForm);
                Set(p, "eText", ser);
                Click(p, "bSearch");
                var g = (UI.Grid)p.Items.Item("gSer").Specific;
                Check(g.Rows.Count == 1, "N° " + ser + " trouvé");
                g.Rows.SelectedRows.Add(0);
                Click(p, "bChoose");
                Check(!IsOpen(FormIds.SerialForm), "Liste fermée après le choix");
                Check(Get(f, "eSerial") == ser && Get(f, "eItem") == "MNTTEST_SER" && Get(f, "eVend") == "F00000001", "Fiche complétée : n°, article, fournisseur");
                Check(Sql.ParseDouble(Get(f, "eAcqV")) == 380000 && Get(f, "eName") != "", "Valeur d'achat et désignation reprises (" + Get(f, "eAcqV") + ")");
                Check(Uds(f, "udSrc").Contains("Réception de marchandises"), "Origine affichée : " + Uds(f, "udSrc"));
                Set(f, UdoForm.KeyItem, eqSer);
                Click(f, "1");
                Pump(600);
                Row r = Sql.First("SELECT * FROM " + Db.T(Db.Equip) + " WHERE \"Code\" = " + Sql.Q(eqSer));
                Check(r != null && r.Str("U_SerialNo") == ser && r.Int("U_SerSys") > 0 && r.Date("U_WarrEnd") == DateTime.Today.AddYears(1), "Équipement enregistré avec son n° de série SAP et sa garantie");

                // Même n° saisi à la main sur une autre fiche : refusé
                NewRecord(f);
                Set(f, UdoForm.KeyItem, "UIX" + S);
                Set(f, "eName", "Doublon UI");
                Click(f, "fTech");
                Set(f, "eItem", "MNTTEST_SER");
                Set(f, "eSerial", ser);
                Click(f, "1");
                Check(!Sql.Exists("SELECT 1 FROM " + Db.T(Db.Equip) + " WHERE \"Code\" = " + Sql.Q("UIX" + S)), "Doublon de n° de série refusé");
                Check(StatusErrors.Any(s => s.Contains("déjà celui de l'équipement")), "Refus affiché dans la barre d'état");
                CloseForm(FormIds.EquipForm);
            }, errorsExpected: true);

            Step("Ordre : pièces de l'équipement, sécurité, bon de travail", () =>
            {
                Navigator.Open(Obj.Order, _direct.ToString(CultureInfo.InvariantCulture));
                Pump();
                UI.Form f = Form(FormIds.OrderForm);
                Check(f.Mode == UI.BoFormMode.fm_OK_MODE && Get(f, UdoForm.KeyItem) == _direct.ToString(CultureInfo.InvariantCulture), "Ordre rouvert");
                Click(f, "fComps");
                int rows = ((UI.Matrix)f.Items.Item("mComps").Specific).RowCount;
                Click(f, "bParts");
                Check(IsOpen(FormIds.SparePartsForm), "Pièces de rechange de l'équipement proposées");
                UI.Form p = Form(FormIds.SparePartsForm);
                Cell(p, "mSpr", "cQty", 1, "2");
                Click(p, "bAdd");
                Check(!IsOpen(FormIds.SparePartsForm), "Fenêtre fermée");
                Check(((UI.Matrix)f.Items.Item("mComps").Specific).RowCount == rows + 1 && f.Mode == UI.BoFormMode.fm_UPDATE_MODE, "Pièce ajoutée aux composants");
                Click(f, "fSafe");
                ((UI.EditText)f.Items.Item("eSafe").Specific).Value = "Consigner l'armoire électrique.";
                Pump(150);
                Click(f, "1");
                Pump(800);
                Check(Sql.Exists("SELECT 1 FROM " + Db.T(Db.OrderComps) + " WHERE \"DocEntry\" = " + _direct + " AND \"U_ItemCode\" = 'CAS_HNKB_50' AND \"U_Qty\" = 2"), "Composant enregistré (quantité 2)");
                Check(Sql.ScalarStr("SELECT CAST(\"U_Safety\" AS NVARCHAR(400)) FROM " + Db.T(Db.Order) + " WHERE \"DocEntry\" = " + _direct) == "Consigner l'armoire électrique.", "Consignes de sécurité enregistrées");
                int before = Opened.Count;
                Click(f, "bPrint");
                Check(Opened.Count == before + 1 && System.Net.WebUtility.HtmlDecode(File.ReadAllText(Opened.Last())).Contains("Consigner l'armoire électrique") && File.ReadAllText(Opened.Last()).Contains("CAS_HNKB_50"),
                      "Bon de travail ouvert : " + Opened.LastOrDefault());
                CloseForm(FormIds.OrderForm);
            });

            Step("Avis urgent : message aux responsables", () =>
            {
                var s = SettingsService.Load();
                s.AlertUsers = DiCompany.UserCode;
                SettingsService.Save(s);
                Menu(FormIds.MenuNotif, FormIds.NotifForm);
                UI.Form f = Form(FormIds.NotifForm);
                NewRecord(f);
                Set(f, "eEq", eq);
                ((UI.ComboBox)f.Items.Item("cPrio").Specific).Select("1", UI.BoSearchKey.psk_ByValue);
                Pump(150);
                Set(f, "eSubj", "Fumée UI " + S);
                int maxMsg = (int)Sql.ScalarDbl("SELECT ISNULL(MAX(\"Code\"), 0) FROM \"OALR\"");
                Click(f, "1");
                Pump(800);
                Check(Sql.Exists("SELECT 1 FROM \"OALR\" WHERE \"Code\" > " + maxMsg + " AND \"Subject\" LIKE " + Sql.Q("%avis urgent%Fumée UI " + S + "%")), "Message urgent dans la messagerie SAP");
                Check(StatusAll.Any(m => m.Contains("prévenus")), "Utilisateur informé dans la barre d'état");
                int n = (int)Sql.ScalarDbl("SELECT MAX(\"DocEntry\") FROM " + Db.T(Db.Notif) + " WHERE \"U_Subject\" = " + Sql.Q("Fumée UI " + S));
                if (n > 0) NotificationService.Complete(n, DateTime.Today);
                CloseForm(FormIds.NotifForm);
            });

            // ---- Paramètres et rapports -----------------------------------------
            Step("Paramètres : enregistrement, alertes", () =>
            {
                Menu(FormIds.MenuSetup, FormIds.SetupForm);
                UI.Form f = Form(FormIds.SetupForm);
                Check(Uds(f, "udExp") == SettingsService.Load().ExpenseAccount, "Compte de charges affiché (" + Uds(f, "udExp") + ")");
                Check(Uds(f, "udAlert") == DiCompany.UserCode, "Destinataires des alertes affichés (" + Uds(f, "udAlert") + ")");
                Click(f, "bSave");
                Check(StatusAll.Any(s => s.Contains("enregistrés")), "Enregistré");
                Click(f, "bAlert");
                Check(StatusAll.Any(s => s.Contains("Alertes envoyées") || s.Contains("Rien à signaler")), "Envoi des alertes à la demande");
                CloseForm(FormIds.SetupForm);
            });

            Step("Rapports : chaque vue", () =>
            {
                Menu(FormIds.MenuReportPrefix + ReportService.Views[0].Code, FormIds.ListForm);
                UI.Form f = Form(FormIds.ListForm);
                foreach (ReportView v in ReportService.Views)
                {
                    ((UI.ComboBox)f.Items.Item("cView").Specific).Select(v.Code, UI.BoSearchKey.psk_ByValue);
                    Pump(150);
                    Click(f, "bSearch");
                    Check(f.Title.Contains(v.Title), "Vue " + v.Code + " : " + Uds(f, "udTot"));
                }
                CloseForm(FormIds.ListForm);
            });
        }

        private static void Cleanup()
        {
            CloseAll();
            foreach (int o in Orders.Distinct())
            {
                string st = OrderService.Status(o);
                if (st == OrderStatus.Created || st == OrderStatus.Released)
                    OrderService.Cancel(o);
            }
            string tsk = "UIG" + S;
            if (Sql.Exists("SELECT 1 FROM " + Db.T(Db.TaskList) + " WHERE \"Code\" = " + Sql.Q(tsk)))
                UdoData.Delete(Obj.TaskList, tsk);
            Check(Orders.All(o => OrderService.Status(o) == OrderStatus.Closed || OrderService.Status(o) == OrderStatus.Cancelled), "Ordres de test clôturés ou annulés");
            // Fichiers de test retirés du dossier partagé des pièces jointes de SAP
            foreach (string f in ShareFiles)
                try { File.Delete(f); } catch { }
        }
    }
}
