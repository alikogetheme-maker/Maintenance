using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using SAPbobsCOM;
using MaintenanceAddon.Core;

namespace MaintenanceAddon.Services
{
    /// <summary>
    /// Documents d'un équipement (notices, photos, schémas, certificats) dans
    /// les pièces jointes standard de SAP : fichiers copiés dans le dossier des
    /// pièces jointes de la société, lignes dans ATC1, n° dans U_AtcEntry.
    /// </summary>
    internal static class AttachmentService
    {
        /// <summary>Lignes jointes (Key = n° de ligne) pour la grille de la fiche.</summary>
        public static string FilesSql(int atcEntry)
        {
            return "SELECT \"Line\" AS \"Key\", \"FileName\" + CASE WHEN ISNULL(\"FileExt\", '') = '' THEN N'' ELSE N'.' + \"FileExt\" END AS \"Fichier\", " +
                   "\"Date\" AS \"Date\", ISNULL(u.\"U_NAME\", N'') AS \"Util\", \"trgtPath\" AS \"Chemin\" " +
                   "FROM \"ATC1\" a LEFT JOIN \"OUSR\" u ON u.\"USERID\" = a.\"UsrID\" WHERE a.\"AbsEntry\" = " + atcEntry + " ORDER BY \"Line\"";
        }

        public static List<Row> Files(int atcEntry)
        {
            return Sql.Rows("SELECT * FROM \"ATC1\" WHERE \"AbsEntry\" = " + atcEntry + " ORDER BY \"Line\"");
        }

        /// <summary>Chemin complet du fichier joint (dossier des pièces jointes).</summary>
        public static string FullPath(int atcEntry, int line)
        {
            Row r = Sql.First("SELECT \"trgtPath\", \"FileName\", \"FileExt\" FROM \"ATC1\" WHERE \"AbsEntry\" = " + atcEntry + " AND \"Line\" = " + line);
            if (r == null)
                return null;
            return Path.Combine(r.Str("trgtPath"), r.Str("FileName") + (r.Str("FileExt") == "" ? "" : "." + r.Str("FileExt")));
        }

        private static string AttachmentFolder()
        {
            string folder = Sql.ScalarStr("SELECT \"AttachPath\" FROM \"OADP\"");
            if (folder == "")
                throw new InvalidOperationException("Le dossier des pièces jointes n'est pas paramétré dans SAP " +
                                                    "(Gestion → Initialisation système → Paramétrage général → onglet Chemin).");
            return folder;
        }

        /// <summary>Joint un fichier à l'équipement ; renvoie le n° de pièces jointes.</summary>
        public static int AddToEquipment(string equip, string file)
        {
            AuthService.Require(Perm.MasterData, "joindre un document à l'équipement");
            if (!File.Exists(file))
                throw new InvalidOperationException("Fichier introuvable : " + file);
            UdoData e = UdoData.Get(Obj.Equip, equip);
            int atc = (int)e.Dbl("U_AtcEntry");

            // Copie temporaire au nom préfixé par l'équipement : pas de collision
            // avec les fichiers d'autres documents dans le dossier commun de SAP
            string folder = AttachmentFolder();
            string ext = Path.GetExtension(file).TrimStart('.');
            string baseName = Clean(equip + "_" + Path.GetFileNameWithoutExtension(file));
            string name = baseName;
            for (int i = 2; File.Exists(Path.Combine(folder, name + (ext == "" ? "" : "." + ext))); i++)
                name = baseName + "_" + i.ToString(CultureInfo.InvariantCulture);
            string tmp = Path.Combine(Path.GetTempPath(), "MaintenanceJoints", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            File.Copy(file, Path.Combine(tmp, name + (ext == "" ? "" : "." + ext)));
            try
            {
                int entry = Save(atc, new[] { new[] { tmp, name, ext } }, true);
                if (entry != atc)
                {
                    e.Set("U_AtcEntry", entry);
                    e.Update();
                }
                return entry;
            }
            finally
            {
                TryDelete(tmp);
            }
        }

        /// <summary>
        /// Retire une ligne : l'API ne supprime pas de ligne de pièce jointe, on
        /// crée donc une nouvelle pièce jointe avec les autres fichiers. Le fichier
        /// retiré reste dans le dossier de SAP (comme dans le client).
        /// </summary>
        public static int RemoveFromEquipment(string equip, int line)
        {
            AuthService.Require(Perm.MasterData, "retirer un document de l'équipement");
            UdoData e = UdoData.Get(Obj.Equip, equip);
            int atc = (int)e.Dbl("U_AtcEntry");
            List<Row> files = Files(atc);
            if (!files.Exists(r => r.Int("Line") == line))
                throw new InvalidOperationException("Document introuvable.");
            var keep = new List<string[]>();
            string tmp = Path.Combine(Path.GetTempPath(), "MaintenanceJoints", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                foreach (Row r in files)
                {
                    if (r.Int("Line") == line)
                        continue;
                    string ext = r.Str("FileExt");
                    string fileName = r.Str("FileName") + (ext == "" ? "" : "." + ext);
                    File.Copy(Path.Combine(r.Str("trgtPath"), fileName), Path.Combine(tmp, fileName));
                    keep.Add(new[] { tmp, r.Str("FileName"), ext });
                }
                int entry = keep.Count == 0 ? 0 : Save(0, keep, true);
                e.Set("U_AtcEntry", entry);
                e.Update();
                return entry;
            }
            finally
            {
                TryDelete(tmp);
            }
        }

        /// <param name="files">Dossier source, nom sans extension, extension.</param>
        private static int Save(int atcEntry, IList<string[]> files, bool overwrite)
        {
            Company company = DiCompany.Instance;
            Attachments2 a = (Attachments2)company.GetBusinessObject(BoObjectTypes.oAttachments2);
            try
            {
                bool exists = atcEntry > 0 && a.GetByKey(atcEntry);
                for (int i = 0; i < files.Count; i++)
                {
                    if (exists || i > 0)
                        a.Lines.Add();
                    a.Lines.SourcePath = files[i][0];
                    a.Lines.FileName = files[i][1];
                    a.Lines.FileExtension = files[i][2];
                    a.Lines.Override = overwrite ? BoYesNoEnum.tYES : BoYesNoEnum.tNO;
                }
                DiCompany.ThrowIfError(exists ? a.Update() : a.Add(), "Pièce jointe");
                return exists ? atcEntry : int.Parse(company.GetNewObjectKey(), CultureInfo.InvariantCulture);
            }
            finally
            {
                Marshal.ReleaseComObject(a);
            }
        }

        private static string Clean(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Length > 100 ? name.Substring(0, 100) : name;
        }

        private static void TryDelete(string dir)
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
