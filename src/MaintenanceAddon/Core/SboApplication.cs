using SAPbouiCOM;
using MaintenanceAddon.Forms;

namespace MaintenanceAddon.Core
{
    /// <summary>
    /// Connexion à l'application UI API et création du menu "Maintenance".
    /// </summary>
    internal static class SboApplication
    {
        public static Application Connect(string connectionString)
        {
            SboGuiApi guiApi = new SboGuiApi();
            guiApi.Connect(connectionString);
            return guiApi.GetApplication(-1);
        }

        // Menu standard "Modules" de SAP B1 (menu principal à gauche) // 
        private const string ModulesMenuUid = "43520";

        public static void CreateMenus(Application app)
        {
            if (app.Menus.Exists(FormIds.MenuRoot))
                app.Menus.RemoveEx(FormIds.MenuRoot);

            MenuItem root = AddRoot(app);

            MenuItem md = Add(app, root, FormIds.MenuMasterData, "Données de base", true);
            Add(app, md, FormIds.MenuFuncLoc, "Postes techniques", false);
            Add(app, md, FormIds.MenuEquip, "Équipements", false);
            Add(app, md, FormIds.MenuTaskList, "Gammes de maintenance", false);
            Add(app, md, FormIds.MenuWorkCtr, "Postes de travail", false);
            Add(app, md, FormIds.MenuCatalog, "Catalogues (dommages, causes...)", false);
            Add(app, md, FormIds.MenuEqCat, "Catégories d'équipement", false);
            Add(app, md, FormIds.MenuContract, "Contrats de maintenance", false);

            Add(app, root, FormIds.MenuNotif, "Avis de maintenance", false);
            Add(app, root, FormIds.MenuOrder, "Ordres de maintenance", false);
            Add(app, root, FormIds.MenuMeasure, "Relevés de compteurs et mesures", false);
            Add(app, root, FormIds.MenuShip, "Envoi / retour chez un prestataire", false);

            MenuItem prev = Add(app, root, FormIds.MenuPreventive, "Maintenance préventive", true);
            Add(app, prev, FormIds.MenuPlan, "Plans de maintenance", false);
            Add(app, prev, FormIds.MenuSched, "Ordonnancement des plans", false);

            MenuItem rep = Add(app, root, FormIds.MenuReports, "Rapports", true);
            foreach (var view in Services.ReportService.Views)
                Add(app, rep, FormIds.MenuReportPrefix + view.Code, view.Title, false);

            Add(app, root, FormIds.MenuSetup, "Paramètres", false);
        }

        private static MenuItem AddRoot(Application app)
        {
            MenuCreationParams p = (MenuCreationParams)app.CreateObject(BoCreatableObjectType.cot_MenuCreationParams);
            p.Type = BoMenuType.mt_POPUP;
            p.UniqueID = FormIds.MenuRoot;
            p.String = "Maintenance";
            p.Position = -1;
            string image = ExtractMenuImage();
            if (image != null)
                p.Image = image;

            try
            {
                return app.Menus.Item(ModulesMenuUid).SubMenus.AddEx(p);
            }
            catch (System.Exception ex) when (image != null)
            {
                // Image refusée par le client : on crée le menu sans icône
                Program.Log("Menu avec icône refusé, création sans icône : " + ex.Message);
                p.Image = "";
                return app.Menus.Item(ModulesMenuUid).SubMenus.AddEx(p);
            }
        }

        private static MenuItem Add(Application app, MenuItem parent, string uid, string caption, bool popup)
        {
            MenuCreationParams p = (MenuCreationParams)app.CreateObject(BoCreatableObjectType.cot_MenuCreationParams);
            p.Type = popup ? BoMenuType.mt_POPUP : BoMenuType.mt_STRING;
            p.UniqueID = uid;
            p.String = caption;
            p.Position = parent.SubMenus.Count;
            return parent.SubMenus.AddEx(p);
        }

        /// <summary>
        /// SAP attend un chemin de fichier BMP : on extrait l'icône intégrée
        /// à l'exe dans le dossier temporaire. Sans icône, le menu est créé quand même.
        /// </summary>
        private static string ExtractMenuImage()
        {
            try
            {
                string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MaintenanceMenu.bmp");
                using (var res = typeof(SboApplication).Assembly.GetManifestResourceStream("MaintenanceAddon.Resources.Maintenance.bmp"))
                {
                    if (res == null)
                        return null;
                    using (var file = System.IO.File.Create(path))
                        res.CopyTo(file);
                }
                return path;
            }
            catch (System.Exception ex)
            {
                Program.Log("Icône du menu non extraite : " + ex.Message);
                return null;
            }
        }
    }
}
