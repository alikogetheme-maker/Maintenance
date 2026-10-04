using SAPbouiCOM;
using MaintenanceAddon.Models;

namespace MaintenanceAddon.Core
{
    /// <summary>
    /// Aide à la construction des écrans UI API par code. Les identifiants
    /// d'élément font au plus 10 caractères (contrainte SAP).
    /// </summary>
    internal sealed class Ui
    {
        public const int RowH = 15;
        public const int Step = 17;

        public readonly Application App;
        public readonly Form F;

        /// <summary>Table DB par défaut des liaisons ("@MNT_OORD"), vide pour un écran sans objet.</summary>
        public string Table;

        /// <summary>Onglet sur lequel placer les éléments suivants (0 = tous les onglets).</summary>
        public int Pane;

        public Ui(Application app, Form form, string table)
        {
            App = app;
            F = form;
            Table = table;
        }

        private Item Add(string id, BoFormItemTypes type, int left, int top, int width, int height)
        {
            Item item = F.Items.Add(id, type);
            item.Left = left;
            item.Top = top;
            item.Width = width;
            item.Height = height;
            if (Pane > 0)
            {
                item.FromPane = Pane;
                item.ToPane = Pane;
            }
            return item;
        }

        public StaticText Label(string id, string caption, int left, int top, int width, string linkTo = null)
        {
            Item item = Add(id, BoFormItemTypes.it_STATIC, left, top, width, RowH);
            if (!string.IsNullOrEmpty(linkTo))
                item.LinkTo = linkTo;
            StaticText st = (StaticText)item.Specific;
            st.Caption = caption;
            return st;
        }

        /// <summary>Zone de saisie liée à un champ de la table par défaut.</summary>
        public EditText Edit(string id, int left, int top, int width, string alias)
        {
            Item item = Add(id, BoFormItemTypes.it_EDIT, left, top, width, RowH);
            EditText e = (EditText)item.Specific;
            e.DataBind.SetBound(true, Table, alias);
            return e;
        }

        /// <summary>Zone de saisie liée à une source utilisateur (non enregistrée dans l'objet).</summary>
        public EditText EditUds(string id, int left, int top, int width, string uds)
        {
            Item item = Add(id, BoFormItemTypes.it_EDIT, left, top, width, RowH);
            EditText e = (EditText)item.Specific;
            e.DataBind.SetBound(true, "", uds);
            return e;
        }

        /// <summary>Libellé + zone de saisie liée ; renvoie la zone.</summary>
        public EditText Field(string labelId, string caption, string id, int left, int top, int labelWidth, int width, string alias)
        {
            Label(labelId, caption, left, top, labelWidth, id);
            return Edit(id, left + labelWidth, top, width, alias);
        }

        /// <summary>Champ en lecture seule (affichage d'un libellé calculé).</summary>
        public EditText ReadOnlyUds(string id, int left, int top, int width, string uds)
        {
            EditText e = EditUds(id, left, top, width, uds);
            Item item = F.Items.Item(id);
            item.AffectsFormMode = false;
            item.Enabled = false;
            return e;
        }

        public ComboBox Combo(string id, int left, int top, int width, string alias, CodeList values = null, bool blank = false)
        {
            Item item = Add(id, BoFormItemTypes.it_COMBO_BOX, left, top, width, RowH);
            item.DisplayDesc = true;
            ComboBox c = (ComboBox)item.Specific;
            c.DataBind.SetBound(true, Table, alias);
            // Champ avec valeurs valides définies dans SAP : la liste est déjà
            // remplie par le client et ne peut pas être vidée (erreur -10).
            if (c.ValidValues.Count == 0)
                Fill(c, values, blank);
            return c;
        }

        public ComboBox ComboUds(string id, int left, int top, int width, string uds, CodeList values = null, bool blank = false)
        {
            Item item = Add(id, BoFormItemTypes.it_COMBO_BOX, left, top, width, RowH);
            item.DisplayDesc = true;
            ComboBox c = (ComboBox)item.Specific;
            c.DataBind.SetBound(true, "", uds);
            Fill(c, values, blank);
            return c;
        }

        public static void Fill(ComboBox c, CodeList values, bool blank)
        {
            while (c.ValidValues.Count > 0)
                c.ValidValues.Remove(0, BoSearchKey.psk_Index);
            if (blank)
                c.ValidValues.Add("", "");
            if (values != null)
                foreach (var v in values.Items)
                    c.ValidValues.Add(v.Key, v.Value);
        }

        public CheckBox Check(string id, string caption, int left, int top, int width, string alias)
        {
            Item item = Add(id, BoFormItemTypes.it_CHECK_BOX, left, top, width, RowH);
            CheckBox c = (CheckBox)item.Specific;
            c.Caption = caption;
            c.ValOn = "Y";
            c.ValOff = "N";
            c.DataBind.SetBound(true, Table, alias);
            return c;
        }

        public CheckBox CheckUds(string id, string caption, int left, int top, int width, string uds)
        {
            Item item = Add(id, BoFormItemTypes.it_CHECK_BOX, left, top, width, RowH);
            CheckBox c = (CheckBox)item.Specific;
            c.Caption = caption;
            c.ValOn = "Y";
            c.ValOff = "N";
            c.DataBind.SetBound(true, "", uds);
            return c;
        }

        /// <summary>Texte long (champ mémo).</summary>
        public EditText Memo(string id, int left, int top, int width, int height, string alias)
        {
            Item item = Add(id, BoFormItemTypes.it_EXTEDIT, left, top, width, height);
            EditText e = (EditText)item.Specific;
            e.DataBind.SetBound(true, Table, alias);
            return e;
        }

        public Button Button(string id, string caption, int left, int top, int width)
        {
            Item item = Add(id, BoFormItemTypes.it_BUTTON, left, top, width, 19);
            Button b = (Button)item.Specific;
            b.Caption = caption;
            return b;
        }

        /// <summary>Flèche de lien vers un objet SAP standard (articles, tiers...).</summary>
        public void LinkStd(string id, string linkTo, BoLinkedObject linked)
        {
            Item target = F.Items.Item(linkTo);
            Item item = Add(id, BoFormItemTypes.it_LINKED_BUTTON, target.Left - 19, target.Top + 1, 18, 12);
            item.LinkTo = linkTo;
            ((LinkedButton)item.Specific).LinkedObject = linked;
        }

        /// <summary>Flèche de lien vers un objet de l'add-on (interceptée par l'écran).</summary>
        public void LinkUdo(string id, string linkTo, string objectCode)
        {
            Item target = F.Items.Item(linkTo);
            Item item = Add(id, BoFormItemTypes.it_LINKED_BUTTON, target.Left - 19, target.Top + 1, 18, 12);
            item.LinkTo = linkTo;
            ((LinkedButton)item.Specific).LinkedObjectType = objectCode;
        }

        /// <summary>Onglet ; le premier d'un groupe a groupWith = null.</summary>
        public Folder Folder(string id, string caption, int left, int top, int width, string uds, string groupWith)
        {
            int savedPane = Pane;
            Pane = 0;
            Item item = Add(id, BoFormItemTypes.it_FOLDER, left, top, width, 19);
            Pane = savedPane;
            item.AffectsFormMode = false;
            Folder f = (Folder)item.Specific;
            f.Caption = caption;
            f.DataBind.SetBound(true, "", uds);
            if (groupWith != null)
                f.GroupWith(groupWith);
            return f;
        }

        /// <summary>Cadre décoratif autour des onglets.</summary>
        public void Frame(string id, int left, int top, int width, int height)
        {
            int savedPane = Pane;
            Pane = 0;
            Add(id, BoFormItemTypes.it_RECTANGLE, left, top, width, height);
            Pane = savedPane;
        }

        public Matrix Matrix(string id, int left, int top, int width, int height)
        {
            Item item = Add(id, BoFormItemTypes.it_MATRIX, left, top, width, height);
            Matrix m = (Matrix)item.Specific;
            m.SelectionMode = BoMatrixSelect.ms_Single;
            return m;
        }

        /// <summary>Colonne de matrice liée à une table enfant.</summary>
        public Column Col(Matrix m, string id, string caption, int width, string childTable, string alias, bool editable,
                          BoFormItemTypes type = BoFormItemTypes.it_EDIT)
        {
            Column c = m.Columns.Add(id, type);
            c.TitleObject.Caption = caption;
            c.Width = width;
            c.Editable = editable;
            if (type == BoFormItemTypes.it_CHECK_BOX)
            {
                // Avant la liaison, sinon une case décochée est enregistrée vide au lieu de "N"
                c.ValOn = "Y";
                c.ValOff = "N";
            }
            c.DataBind.SetBound(true, childTable, alias);
            return c;
        }

        public Grid Grid(string id, string dtId, int left, int top, int width, int height)
        {
            // La table peut déjà avoir été créée par l'écran (colonnes définies par code)
            bool exists = false;
            for (int i = 0; i < F.DataSources.DataTables.Count && !exists; i++)
                exists = F.DataSources.DataTables.Item(i).UniqueID == dtId;
            if (!exists)
                F.DataSources.DataTables.Add(dtId);
            Item item = Add(id, BoFormItemTypes.it_GRID, left, top, width, height);
            Grid g = (Grid)item.Specific;
            g.SelectionMode = BoMatrixSelect.ms_Single;
            return g;
        }

        /// <summary>
        /// Remplit une grille en lecture seule : libellés de colonnes pris dans
        /// ReportService.Captions, colonnes techniques Key / KeyObj masquées.
        /// </summary>
        public DataTable LoadGrid(string gridId, string dtId, string sql)
        {
            DataTable dt = F.DataSources.DataTables.Item(dtId);
            dt.ExecuteQuery(sql);
            Grid grid = (Grid)F.Items.Item(gridId).Specific;
            grid.DataTable = dt;
            for (int i = 0; i < grid.Columns.Count; i++)
            {
                GridColumn col = grid.Columns.Item(i);
                col.Editable = false;
                if (col.UniqueID == "Key" || col.UniqueID == "KeyObj")
                    col.Visible = false;
                else if (Services.ReportService.Captions.TryGetValue(col.UniqueID, out string caption))
                    col.TitleObject.Caption = caption;
            }
            grid.AutoResizeColumns();
            return dt;
        }

        public static bool IsEmpty(DataTable dt)
        {
            return dt.IsEmpty || dt.Rows.Count == 0;
        }

        /// <summary>Liste de choix (CFL) sur un objet SAP ("4" articles, "2" tiers, "64" magasins, "61" centres de coûts, "171" salariés) ou un UDO.</summary>
        public ChooseFromList Cfl(string uid, string objectType)
        {
            ChooseFromListCreationParams p = (ChooseFromListCreationParams)App.CreateObject(BoCreatableObjectType.cot_ChooseFromListCreationParams);
            p.UniqueID = uid;
            p.ObjectType = objectType;
            p.MultiSelection = false;
            return F.ChooseFromLists.Add(p);
        }

        /// <summary>Restreint une CFL à alias = valeur.</summary>
        public static void CflFilter(ChooseFromList cfl, string alias, string value)
        {
            Conditions cons = cfl.GetConditions();
            Condition c = cons.Add();
            c.Alias = alias;
            c.Operation = BoConditionOperation.co_EQUAL;
            c.CondVal = value;
            cfl.SetConditions(cons);
        }

        public static void BindCfl(EditText e, string cflUid, string alias)
        {
            e.ChooseFromListUID = cflUid;
            e.ChooseFromListAlias = alias;
        }

        public static void BindCfl(Column c, string cflUid, string alias)
        {
            c.ChooseFromListUID = cflUid;
            c.ChooseFromListAlias = alias;
        }

        /// <summary>Élément modifiable seulement dans les modes indiqués (Ajout, Recherche, OK/Mise à jour).</summary>
        public void Editable(string id, bool add, bool find, bool ok)
        {
            Item item = F.Items.Item(id);
            item.SetAutoManagedAttribute(BoAutoManagedAttr.ama_Editable, (int)BoAutoFormMode.afm_Add, add ? BoModeVisualBehavior.mvb_True : BoModeVisualBehavior.mvb_False);
            item.SetAutoManagedAttribute(BoAutoManagedAttr.ama_Editable, (int)BoAutoFormMode.afm_Find, find ? BoModeVisualBehavior.mvb_True : BoModeVisualBehavior.mvb_False);
            item.SetAutoManagedAttribute(BoAutoManagedAttr.ama_Editable, (int)BoAutoFormMode.afm_Ok, ok ? BoModeVisualBehavior.mvb_True : BoModeVisualBehavior.mvb_False);
        }
    }
}
