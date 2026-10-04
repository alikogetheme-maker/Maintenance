using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using SAPbouiCOM;

namespace MaintenanceAddon.Core
{
    /// <summary>
    /// Écran lié à un objet UDO : SAP gère nativement les modes Ajout /
    /// Recherche / OK / Mise à jour, la navigation (premier, suivant...) et
    /// l'enregistrement. La classe ajoute : matrices de lignes (ajout /
    /// suppression), listes de choix, onglets, liens vers les autres objets
    /// de l'add-on, contrôles avant enregistrement.
    /// Une seule fenêtre par type d'objet (UniqueID = FormType).
    /// </summary>
    internal abstract class UdoForm
    {
        public const string KeyItem = "eKey";
        public const string DocNumItem = "eDocNum";
        private const string UdFolder = "udFold";

        protected readonly Application App;
        protected Form F;
        protected Ui U;

        private sealed class MatrixInfo
        {
            public string ItemId;
            public string Table;
            public string KeyAlias;
            public string AddButton;
            public string DelButton;
            public int LastRow;
            public Action<DBDataSource, int> OnNewLine;
        }

        private sealed class CflTarget
        {
            public string Table;
            public string Alias;
            public string ReturnColumn;
        }

        private readonly List<MatrixInfo> _matrices = new List<MatrixInfo>();
        private readonly Dictionary<string, CflTarget> _cfls = new Dictionary<string, CflTarget>();
        private readonly Dictionary<string, string> _udoLinks = new Dictionary<string, string>();
        private readonly Dictionary<string, int> _folders = new Dictionary<string, int>();
        private readonly Dictionary<string, string[]> _grids = new Dictionary<string, string[]>();
        private string _firstFolder;
        private bool _adding;
        private bool _saving;
        private string _savedKey;
        private string _addedKey;
        private string _pendingCode;
        /// <summary>Après une création : valeurs par défaut à remettre quand SAP a vidé l'écran.</summary>
        private bool _defaultsPending;

        /// <summary>Code saisi (données de base) ou dernier document créé par l'utilisateur connecté.</summary>
        private string LastCreatedKey()
        {
            if (!IsDocument)
                return string.IsNullOrEmpty(_pendingCode) ? null : _pendingCode;
            double entry = Sql.ScalarDbl(
                "SELECT MAX(\"DocEntry\") FROM \"@" + HeaderTable + "\" WHERE \"UserSign\" = " +
                "(SELECT \"USERID\" FROM \"OUSR\" WHERE \"USER_CODE\" = " + Sql.Q(DiCompany.UserCode) + ")");
            return entry > 0 ? ((int)entry).ToString(CultureInfo.InvariantCulture) : null;
        }

        protected UdoForm(Application app)
        {
            App = app;
            EventHub.RegisterForm(app, FormType, App_ItemEvent, App_FormDataEvent);
            EventHub.RegisterMenu(app, App_MenuEvent);
        }

        // ---------------------------------------------------------------------
        // À définir par chaque écran
        // ---------------------------------------------------------------------

        public abstract string ObjectCode { get; }
        protected abstract string FormType { get; }
        protected abstract string HeaderTable { get; }
        protected abstract string[] ChildTables { get; }
        protected abstract bool IsDocument { get; }
        protected abstract string Title { get; }
        protected abstract int FormWidth { get; }
        protected abstract int FormHeight { get; }

        /// <summary>Construit les éléments (U est prêt, lié à la table d'en-tête).</summary>
        protected abstract void Build();

        /// <summary>Valeurs par défaut d'un nouvel enregistrement.</summary>
        protected virtual void SetDefaults() { }

        /// <summary>Contrôle avant ajout / mise à jour ; renvoie un message d'erreur ou null.</summary>
        protected virtual string Validate() { return null; }

        /// <summary>Contrôle avant suppression ; renvoie un message d'erreur ou null.</summary>
        protected virtual string CanDelete() { return null; }

        /// <summary>Contrôle avant suppression d'une ligne de matrice (row = 0..n-1).</summary>
        protected virtual string CanDeleteLine(string table, DBDataSource ds, int row) { return null; }

        /// <summary>Met à jour les libellés calculés et l'état des boutons.</summary>
        protected virtual void Refresh() { }

        protected virtual void OnButton(string itemUid) { }
        protected virtual void OnChosen(string itemUid, string colUid, int row, DataTable selected) { }
        protected virtual void BeforeChoose(string itemUid, string colUid, int row, ChooseFromList cfl) { }
        protected virtual void OnComboSelect(string itemUid, string colUid, int row) { }
        protected virtual void OnValidate(string itemUid, string colUid, int row) { }

        /// <summary>Après un ajout ou une mise à jour réussis (clé de l'enregistrement).</summary>
        protected virtual void AfterSaved(string key, bool added) { }

        // ---------------------------------------------------------------------
        // Ouverture
        // ---------------------------------------------------------------------

        public bool IsOpen => F != null;

        public void Show()
        {
            if (F != null)
            {
                F.Select();
                return;
            }
            Create();
        }

        /// <summary>Ouvre l'écran sur l'enregistrement (DocEntry ou Code).</summary>
        public void OpenKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;
            Show();
            if (F.Mode == BoFormMode.fm_UPDATE_MODE || (F.Mode == BoFormMode.fm_ADD_MODE && IsDirtyAdd()))
            {
                if (Program.Message(App, "Des modifications ne sont pas enregistrées dans « " + Title + " ». Les abandonner ?", true) != 1)
                    return;
            }
            F.Mode = BoFormMode.fm_FIND_MODE;
            ((EditText)F.Items.Item(KeyItem).Specific).Value = key;
            F.Items.Item("1").Click(BoCellClickType.ct_Regular);
        }

        /// <summary>Ouvre l'écran en création, pré-rempli par l'appelant.</summary>
        public void ShowNew(Action prefill)
        {
            Show();
            if (F.Mode != BoFormMode.fm_ADD_MODE)
            {
                if (F.Mode == BoFormMode.fm_UPDATE_MODE &&
                    Program.Message(App, "Des modifications ne sont pas enregistrées dans « " + Title + " ». Les abandonner ?", true) != 1)
                    return;
                F.Mode = BoFormMode.fm_ADD_MODE;
            }
            InitAddMode();
            if (prefill != null)
            {
                prefill();
                SafeRefresh();
            }
        }

        private bool IsDirtyAdd()
        {
            // En création, on considère qu'il y a une saisie dès qu'une ligne existe
            // (lignes affichées : la source garde un enregistrement vide en création)
            foreach (MatrixInfo mi in _matrices)
                if (Mat(mi.ItemId).RowCount > 0)
                    return true;
            return false;
        }

        private void Create()
        {
            FormCreationParams p = (FormCreationParams)App.CreateObject(BoCreatableObjectType.cot_FormCreationParams);
            p.FormType = FormType;
            p.UniqueID = FormType;
            p.ObjectType = ObjectCode;
            p.BorderStyle = BoFormBorderStyle.fbs_Fixed;

            F = App.Forms.AddEx(p);
            F.Freeze(true);
            try
            {
                F.Title = Title;
                F.Width = FormWidth;
                F.Height = FormHeight;
                F.AutoManaged = true;

                F.DataSources.DBDataSources.Add("@" + HeaderTable);
                foreach (string child in ChildTables)
                    F.DataSources.DBDataSources.Add("@" + child);
                F.DataSources.UserDataSources.Add(UdFolder, BoDataType.dt_SHORT_TEXT, 1);

                U = new Ui(App, F, "@" + HeaderTable);
                _matrices.Clear();
                _cfls.Clear();
                _udoLinks.Clear();
                _folders.Clear();
                _grids.Clear();
                _firstFolder = null;

                Build();

                U.Pane = 0;
                int top = FormHeight - 62;
                U.Button("1", "OK", 6, top, 65);
                U.Button("2", "Annuler", 76, top, 65);

                F.DataBrowser.BrowseBy = KeyItem;
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

            F.Mode = BoFormMode.fm_ADD_MODE;
            F.Visible = true;
            if (_firstFolder != null)
            {
                ((Folder)F.Items.Item(_firstFolder).Specific).Select();
                F.PaneLevel = 1;
            }
            InitAddMode();
        }

        private void InitAddMode()
        {
            if (IsDocument)
                SetNextNumber();
            foreach (MatrixInfo mi in _matrices)
            {
                DBDataSource ds = Lines(mi.Table);
                while (ds.Size > 0)
                    ds.RemoveRecord(0);
                ((Matrix)F.Items.Item(mi.ItemId).Specific).LoadFromDataSource();
                mi.LastRow = 0;
            }
            SetDefaults();
            SafeRefresh();
        }

        /// <summary>Série par défaut de l'objet et prochain numéro (documents).</summary>
        private void SetNextNumber()
        {
            try
            {
                string series = Sql.ScalarStr("SELECT \"DfltSeries\" FROM \"ONNM\" WHERE \"ObjectCode\" = " + Sql.Q(ObjectCode));
                if (string.IsNullOrEmpty(series))
                    return;
                Head.SetValue("Series", 0, series);
                int next = F.BusinessObject.GetNextSerialNumber(series, ObjectCode);
                Head.SetValue("DocNum", 0, next.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                Program.Log("Numérotation " + ObjectCode + " : " + ex.Message);
            }
        }

        // ---------------------------------------------------------------------
        // Outils pour les écrans
        // ---------------------------------------------------------------------

        protected DBDataSource Head => F.DataSources.DBDataSources.Item("@" + HeaderTable);

        protected DBDataSource Lines(string table)
        {
            return F.DataSources.DBDataSources.Item("@" + table);
        }

        protected string H(string alias)
        {
            return Head.GetValue(alias, 0).Trim();
        }

        protected double HDbl(string alias)
        {
            return Sql.ParseDouble(H(alias));
        }

        protected DateTime? HDate(string alias)
        {
            return Sql.ParseDate(H(alias));
        }

        protected void SetH(string alias, string value)
        {
            Head.SetValue(alias, 0, value ?? "");
        }

        protected void SetH(string alias, DateTime? date)
        {
            Head.SetValue(alias, 0, date.HasValue ? date.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture) : "");
        }

        protected void SetH(string alias, double value)
        {
            Head.SetValue(alias, 0, Sql.N(value));
        }

        protected string Uds(string id)
        {
            return F.DataSources.UserDataSources.Item(id).ValueEx;
        }

        protected void SetUds(string id, string value)
        {
            F.DataSources.UserDataSources.Item(id).ValueEx = value ?? "";
        }

        /// <summary>Clé de l'enregistrement affiché (DocEntry ou Code), vide en création.</summary>
        protected string CurrentKey => F == null || F.Mode == BoFormMode.fm_ADD_MODE || F.Mode == BoFormMode.fm_FIND_MODE
            ? ""
            : H(IsDocument ? "DocEntry" : "Code");

        protected int CurrentDocEntry
        {
            get
            {
                int.TryParse(CurrentKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v);
                return v;
            }
        }

        protected bool IsOkMode => F != null && F.Mode == BoFormMode.fm_OK_MODE;

        /// <summary>Les actions métier exigent un enregistrement sauvegardé et sans modification en cours.</summary>
        protected bool RequireSaved()
        {
            if (IsOkMode)
                return true;
            Msg(F.Mode == BoFormMode.fm_UPDATE_MODE
                    ? "Enregistrez d'abord vos modifications (bouton Mettre à jour)."
                    : "Enregistrez ou recherchez d'abord un enregistrement.",
                BoStatusBarMessageType.smt_Warning);
            return false;
        }

        /// <summary>Recharge l'enregistrement affiché (après une action métier).</summary>
        protected void Reload()
        {
            string key = CurrentKey;
            if (string.IsNullOrEmpty(key))
                return;
            F.Mode = BoFormMode.fm_FIND_MODE;
            ((EditText)F.Items.Item(KeyItem).Specific).Value = key;
            F.Items.Item("1").Click(BoCellClickType.ct_Regular);
        }

        protected void Msg(string text, BoStatusBarMessageType type = BoStatusBarMessageType.smt_Success)
        {
            App.StatusBar.SetText(text, BoMessageTime.bmt_Medium, type);
        }

        protected bool Confirm(string question)
        {
            return Program.Message(App, question, true) == 1;
        }

        protected void SetModeUpdate()
        {
            if (F.Mode == BoFormMode.fm_OK_MODE)
                F.Mode = BoFormMode.fm_UPDATE_MODE;
        }

        protected void Enable(string itemId, bool enabled)
        {
            Item item = F.Items.Item(itemId);
            if (item.Enabled != enabled)
            {
                // Un élément actif ne peut pas être désactivé : on déplace le focus.
                // ActiveItem n'est pas lisible quand une autre fenêtre est active.
                if (!enabled)
                {
                    try
                    {
                        if (F.ActiveItem == itemId)
                            F.ActiveItem = IsDocument ? DocNumItem : "eName";
                    }
                    catch { }
                }
                try
                {
                    item.Enabled = enabled;
                }
                catch (Exception ex)
                {
                    Program.Log(FormType + " Enable " + itemId + " : " + ex.Message);
                }
            }
        }

        /// <summary>Champ clé (Code ou DocEntry) + N° pour les documents.</summary>
        protected void BuildKeyFields(int left, int top, int labelWidth)
        {
            if (IsDocument)
            {
                U.Label("lDocNum", "N°", left, top, labelWidth, DocNumItem);
                U.Edit(DocNumItem, left + labelWidth, top, 80, "DocNum");
                U.Editable(DocNumItem, false, true, false);
                U.Label("lKey", "N° interne", left, top + Ui.Step, labelWidth, KeyItem);
                U.Edit(KeyItem, left + labelWidth, top + Ui.Step, 80, "DocEntry");
                U.Editable(KeyItem, false, true, false);
            }
            else
            {
                U.Label("lKey", "Code", left, top, labelWidth, KeyItem);
                U.Edit(KeyItem, left + labelWidth, top, 120, "Code");
                U.Editable(KeyItem, true, true, false);
                U.Label("lName", "Désignation", left, top + Ui.Step, labelWidth, "eName");
                U.Edit("eName", left + labelWidth, top + Ui.Step, 260, "Name");
            }
        }

        /// <summary>Onglet n (1..) : les éléments créés ensuite avec U.Pane = n s'y placent.</summary>
        protected void AddFolder(string id, string caption, int left, int top, int width, int pane)
        {
            U.Folder(id, caption, left, top, width, UdFolder, _firstFolder == null ? null : LastFolder());
            _folders[id] = pane;
            if (_firstFolder == null)
                _firstFolder = id;
            _lastFolder = id;
        }

        private string _lastFolder;
        private string LastFolder() { return _lastFolder; }

        protected void SelectFolder(string id)
        {
            ((Folder)F.Items.Item(id).Specific).Select();
            F.PaneLevel = _folders[id];
        }

        /// <summary>Déclare une matrice de lignes avec ses boutons d'ajout et de suppression.</summary>
        protected void RegisterMatrix(string itemId, string table, string keyAlias, string addButton, string delButton,
                                      Action<DBDataSource, int> onNewLine = null)
        {
            _matrices.Add(new MatrixInfo
            {
                ItemId = itemId,
                Table = table,
                KeyAlias = keyAlias,
                AddButton = addButton,
                DelButton = delButton,
                OnNewLine = onNewLine
            });
        }

        /// <summary>Liste de choix : la valeur choisie (returnColumn) est écrite dans table.alias.</summary>
        protected void RegisterCfl(string itemUid, string colUid, string table, string alias, string returnColumn)
        {
            _cfls[itemUid + "|" + (colUid ?? "")] = new CflTarget { Table = table, Alias = alias, ReturnColumn = returnColumn };
        }

        /// <summary>Flèche de lien vers un objet de l'add-on : ouvre notre écran.</summary>
        protected void RegisterUdoLink(string linkButtonId, string linkTo, string objectCode)
        {
            U.LinkUdo(linkButtonId, linkTo, objectCode);
            _udoLinks[linkButtonId] = objectCode;
        }

        /// <summary>
        /// Grille d'historique : un double-clic ouvre l'objet de la ligne
        /// (colonnes Key et KeyObj, ou Key + objet par défaut).
        /// </summary>
        protected void RegisterGrid(string gridId, string dtId, string defaultObject)
        {
            _grids[gridId] = new[] { dtId, defaultObject ?? "" };
        }

        /// <summary>Ouvre l'objet de la ligne cliquée d'une grille (Key / KeyObj).</summary>
        internal static void OpenGridRow(Form form, string gridId, string dtId, string defaultObject, int gridRow)
        {
            if (gridRow < 0)
                return;
            Grid grid = (Grid)form.Items.Item(gridId).Specific;
            DataTable dt = form.DataSources.DataTables.Item(dtId);
            if (Ui.IsEmpty(dt))
                return;
            int row = grid.GetDataTableRowIndex(gridRow);
            if (row < 0 || row >= dt.Rows.Count)
                return;
            string obj = defaultObject;
            for (int i = 0; i < dt.Columns.Count; i++)
                if (dt.Columns.Item(i).Name == "KeyObj")
                    obj = Convert.ToString(dt.GetValue("KeyObj", row), CultureInfo.InvariantCulture);
            string key = Convert.ToString(dt.GetValue("Key", row), CultureInfo.InvariantCulture).Trim();
            if (!string.IsNullOrEmpty(obj) && key != "" && key != "0")
                Forms.Navigator.Open(obj, key);
        }

        /// <summary>Dernière ligne cliquée (1..n) d'une matrice déclarée, 0 si aucune.</summary>
        protected int LastClickedRow(string matrixId)
        {
            foreach (MatrixInfo mi in _matrices)
                if (mi.ItemId == matrixId)
                    return mi.LastRow;
            return 0;
        }

        protected Matrix Mat(string itemId)
        {
            return (Matrix)F.Items.Item(itemId).Specific;
        }

        /// <summary>Recopie les lignes de matrice dans les sources et enlève les lignes sans clé.</summary>
        protected void FlushMatrices()
        {
            foreach (MatrixInfo mi in _matrices)
            {
                Matrix m = Mat(mi.ItemId);
                m.FlushToDataSource();
                DBDataSource ds = Lines(mi.Table);
                for (int i = ds.Size - 1; i >= 0; i--)
                    if (string.IsNullOrWhiteSpace(ds.GetValue(mi.KeyAlias, i)))
                        ds.RemoveRecord(i);
                m.LoadFromDataSource();
            }
        }

        /// <summary>Recharge une matrice après modification de sa source.</summary>
        protected void ReloadMatrix(string itemId)
        {
            Mat(itemId).LoadFromDataSource();
        }

        private void AddLine(MatrixInfo mi)
        {
            if (F.Mode == BoFormMode.fm_FIND_MODE)
                return;
            Matrix m = Mat(mi.ItemId);
            m.FlushToDataSource();
            DBDataSource ds = Lines(mi.Table);
            // En création, SAP laisse un enregistrement vide non affiché : la nouvelle
            // ligne serait insérée après lui et ses valeurs par défaut perdues
            if (m.RowCount == 0)
                while (ds.Size > 0)
                    ds.RemoveRecord(0);
            int max = 0;
            for (int i = 0; i < ds.Size; i++)
            {
                int.TryParse(ds.GetValue("LineId", i).Trim(), out int id);
                max = Math.Max(max, id);
            }
            ds.InsertRecord(ds.Size);
            int row = ds.Size - 1;
            ds.SetValue("LineId", row, (max + 1).ToString(CultureInfo.InvariantCulture));
            mi.OnNewLine?.Invoke(ds, row);
            m.LoadFromDataSource();
            SetModeUpdate();
            mi.LastRow = row + 1;
        }

        private void DeleteLine(MatrixInfo mi)
        {
            if (F.Mode == BoFormMode.fm_FIND_MODE)
                return;
            Matrix m = Mat(mi.ItemId);
            if (mi.LastRow < 1 || mi.LastRow > m.RowCount)
            {
                Msg("Cliquez d'abord sur la ligne à supprimer.", BoStatusBarMessageType.smt_Warning);
                return;
            }
            m.FlushToDataSource();
            DBDataSource ds = Lines(mi.Table);
            string refusal = CanDeleteLine(mi.Table, ds, mi.LastRow - 1);
            if (refusal != null)
            {
                Msg(refusal, BoStatusBarMessageType.smt_Error);
                return;
            }
            ds.RemoveRecord(mi.LastRow - 1);
            m.LoadFromDataSource();
            mi.LastRow = 0;
            SetModeUpdate();
        }

        private void SafeRefresh()
        {
            if (F == null)
                return;
            BoFormMode before = F.Mode;
            try
            {
                Refresh();
            }
            catch (Exception ex)
            {
                Program.Log(FormType + " Refresh : " + ex);
                Msg(ex.Message, BoStatusBarMessageType.smt_Warning);
            }
            // Les libellés calculés ne sont pas des modifications de l'utilisateur
            try
            {
                if (before == BoFormMode.fm_OK_MODE && F.Mode == BoFormMode.fm_UPDATE_MODE)
                    F.Mode = BoFormMode.fm_OK_MODE;
            }
            catch
            {
                // sans conséquence
            }
        }

        // ---------------------------------------------------------------------
        // Événements
        // ---------------------------------------------------------------------

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
                    bubbleEvent = Before(pVal);
                else
                    After(pVal);
            }
            catch (Exception ex)
            {
                Program.Log(FormType + " : " + ex);
                Program.Message(App, ex.Message);
                if (pVal.BeforeAction)
                    bubbleEvent = false;
            }
        }

        private bool Before(ItemEvent e)
        {
            switch (e.EventType)
            {
                case BoEventTypes.et_ITEM_PRESSED:
                    if (e.ItemUID == "1" && (F.Mode == BoFormMode.fm_ADD_MODE || F.Mode == BoFormMode.fm_UPDATE_MODE))
                    {
                        FlushMatrices();
                        string error = Validate();
                        if (error != null)
                        {
                            Msg(error, BoStatusBarMessageType.smt_Error);
                            return false;
                        }
                        _adding = F.Mode == BoFormMode.fm_ADD_MODE;
                        _pendingCode = IsDocument ? null : H("Code");
                        _saving = true;
                        _savedKey = _adding ? null : CurrentKey;
                        _addedKey = null;
                        return true;
                    }
                    if (_udoLinks.TryGetValue(e.ItemUID, out string obj))
                    {
                        string target = F.Items.Item(e.ItemUID).LinkTo;
                        string value = ((EditText)F.Items.Item(target).Specific).Value.Trim();
                        if (value != "" && value != "0")
                            Forms.Navigator.Open(obj, value);
                        return false;
                    }
                    return true;

                case BoEventTypes.et_CHOOSE_FROM_LIST:
                    {
                        IChooseFromListEvent cfe = (IChooseFromListEvent)e;
                        ChooseFromList cfl = F.ChooseFromLists.Item(cfe.ChooseFromListUID);
                        BeforeChoose(e.ItemUID, e.ColUID, e.Row, cfl);
                        return true;
                    }
            }
            return true;
        }

        private void After(ItemEvent e)
        {
            // Premier événement après une création : SAP a vidé l'écran, on remet les valeurs par défaut
            if (_defaultsPending && !(e.EventType == BoEventTypes.et_ITEM_PRESSED && e.ItemUID == "1"))
            {
                _defaultsPending = false;
                if (F.Mode == BoFormMode.fm_ADD_MODE)
                {
                    SetDefaults();
                    SafeRefresh();
                }
            }

            switch (e.EventType)
            {
                case BoEventTypes.et_ITEM_PRESSED:
                    if (e.ItemUID == "1")
                    {
                        if (!_saving)
                            return;
                        _saving = false;
                        bool added = _adding;
                        _adding = false;
                        string key = added ? _addedKey : _savedKey;
                        _addedKey = null;
                        if (!e.ActionSuccess)
                            return;
                        if (!string.IsNullOrEmpty(key))
                        {
                            try
                            {
                                AfterSaved(key, added);
                            }
                            catch (Exception ex)
                            {
                                Program.Log(FormType + " AfterSaved : " + ex);
                                Program.Message(App, "L'enregistrement est bien fait, mais le traitement complémentaire a échoué : " + ex.Message);
                            }
                        }
                        if (added)
                        {
                            // Comportement standard SAP : l'écran repasse en création (vidé par SAP
                            // APRÈS cet événement). Les valeurs par défaut sont remises au prochain
                            // événement de l'écran ; le document créé se retrouve par la navigation.
                            _defaultsPending = true;
                            if (IsDocument && !string.IsNullOrEmpty(key))
                                Msg(Title + " n° " + Sql.ScalarStr("SELECT \"DocNum\" FROM \"@" + HeaderTable + "\" WHERE \"DocEntry\" = " + key) +
                                    " créé(e). Flèche « dernier enregistrement » ou Rechercher pour le rouvrir.");
                        }
                        return;
                    }
                    if (_folders.TryGetValue(e.ItemUID, out int pane))
                    {
                        F.PaneLevel = pane;
                        return;
                    }
                    foreach (MatrixInfo mi in _matrices)
                    {
                        if (e.ItemUID == mi.AddButton) { AddLine(mi); return; }
                        if (e.ItemUID == mi.DelButton) { DeleteLine(mi); return; }
                    }
                    if (e.ActionSuccess)
                        OnButton(e.ItemUID);
                    return;

                case BoEventTypes.et_CLICK:
                    foreach (MatrixInfo mi in _matrices)
                        if (e.ItemUID == mi.ItemId && e.Row > 0)
                            mi.LastRow = e.Row;
                    return;

                case BoEventTypes.et_DOUBLE_CLICK:
                    if (_grids.TryGetValue(e.ItemUID, out string[] g))
                        OpenGridRow(F, e.ItemUID, g[0], g[1], e.Row);
                    return;

                case BoEventTypes.et_CHOOSE_FROM_LIST:
                    HandleChoose(e);
                    return;

                case BoEventTypes.et_COMBO_SELECT:
                    OnComboSelect(e.ItemUID, e.ColUID, e.Row);
                    return;

                case BoEventTypes.et_VALIDATE:
                    if (e.ItemChanged)
                        OnValidate(e.ItemUID, e.ColUID, e.Row);
                    return;
            }
        }

        private void HandleChoose(ItemEvent e)
        {
            IChooseFromListEvent cfe = (IChooseFromListEvent)e;
            DataTable selected = cfe.SelectedObjects;
            if (selected == null || selected.Rows.Count == 0)
                return;

            bool inMatrix = !string.IsNullOrEmpty(e.ColUID) && e.Row > 0;
            _cfls.TryGetValue(e.ItemUID + "|" + (inMatrix ? e.ColUID : ""), out CflTarget target);

            if (inMatrix)
            {
                Matrix m = Mat(e.ItemUID);
                m.FlushToDataSource();
                string chosen = target == null ? null : ChosenValue(selected, target.ReturnColumn);
                if (chosen != null)
                    F.DataSources.DBDataSources.Item(target.Table).SetValue(target.Alias, e.Row - 1, chosen);
                OnChosen(e.ItemUID, e.ColUID, e.Row, selected);
                m.LoadFromDataSource();
            }
            else
            {
                string value = target == null ? null : ChosenValue(selected, target.ReturnColumn);
                if (value != null)
                {
                    if (string.IsNullOrEmpty(target.Table))
                        F.DataSources.UserDataSources.Item(target.Alias).ValueEx = value;
                    else
                        F.DataSources.DBDataSources.Item(target.Table).SetValue(target.Alias, 0, value);
                }
                OnChosen(e.ItemUID, null, 0, selected);
            }

            if (target == null || !string.IsNullOrEmpty(target.Table))
                SetModeUpdate();
            SafeRefresh();
        }

        /// <summary>
        /// Valeur d'une colonne de la sélection ; null si la colonne n'existe pas
        /// (selon le mode, SAP ne renvoie pas toujours les mêmes colonnes ; la valeur
        /// liée au champ est de toute façon reportée par SAP).
        /// </summary>
        private static string ChosenValue(DataTable selected, string column)
        {
            try
            {
                return Convert.ToString(selected.GetValue(column, 0), CultureInfo.InvariantCulture);
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                return null;
            }
        }

        private void App_FormDataEvent(ref BusinessObjectInfo info, out bool bubbleEvent)
        {
            bubbleEvent = true;
            if (info.FormUID != FormType || F == null)
                return;

            try
            {
                if (info.BeforeAction)
                {
                    if (info.EventType == BoEventTypes.et_FORM_DATA_DELETE)
                    {
                        string refusal = CanDelete();
                        if (refusal != null)
                        {
                            Msg(refusal, BoStatusBarMessageType.smt_Error);
                            bubbleEvent = false;
                        }
                    }
                    return;
                }

                if (!info.ActionSuccess)
                    return;

                switch (info.EventType)
                {
                    case BoEventTypes.et_FORM_DATA_LOAD:
                        foreach (MatrixInfo mi in _matrices)
                            mi.LastRow = 0;
                        SafeRefresh();
                        break;
                    case BoEventTypes.et_FORM_DATA_ADD:
                        // ObjectKey est vide pour les écrans créés par code : clé retrouvée en base
                        _addedKey = ParseKey(info.ObjectKey) ?? LastCreatedKey();
                        break;
                    case BoEventTypes.et_FORM_DATA_UPDATE:
                        SafeRefresh();
                        break;
                }
            }
            catch (Exception ex)
            {
                Program.Log(FormType + " FormData : " + ex);
                Program.Message(App, ex.Message);
            }
        }

        private void App_MenuEvent(ref MenuEvent pVal, out bool bubbleEvent)
        {
            bubbleEvent = true;
            if (pVal.BeforeAction || F == null || pVal.MenuUID != "1282")
                return;
            try
            {
                if (App.Forms.ActiveForm.UniqueID == FormType)
                    InitAddMode();
            }
            catch (Exception ex)
            {
                Program.Log(FormType + " Menu : " + ex.Message);
            }
        }

        private static string ParseKey(string objectKey)
        {
            if (string.IsNullOrEmpty(objectKey))
                return null;
            Match m = Regex.Match(objectKey, "<DocEntry>\\s*(\\d+)\\s*</DocEntry>");
            if (m.Success)
                return m.Groups[1].Value;
            m = Regex.Match(objectKey, "<Code>\\s*(.*?)\\s*</Code>");
            return m.Success ? m.Groups[1].Value : null;
        }
    }
}
