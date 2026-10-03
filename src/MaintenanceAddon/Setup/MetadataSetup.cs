using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SAPbobsCOM;
using MaintenanceAddon.Core;
using MaintenanceAddon.Models;

namespace MaintenanceAddon.Setup
{
    /// <summary>
    /// Crée au démarrage (uniquement ce qui manque) les tables, champs et
    /// objets UDO de l'add-on, puis le paramétrage et les données de départ.
    /// Idempotent : peut être appelé à chaque démarrage sans risque.
    /// </summary>
    internal static class MetadataSetup
    {
        /// <summary>Message d'avancement (barre d'état), facultatif.</summary>
        public static Action<string> Progress;

        private static bool _changed;

        /// <returns>true si quelque chose a été créé (les autres utilisateurs doivent se reconnecter).</returns>
        public static bool EnsureSchema()
        {
            _changed = false;
            CreateTables();
            CreateDocumentFields();
            RegisterObjects();
            EnsureSetupRow();
            SeedData();
            return _changed;
        }

        // =====================================================================
        // Tables et champs
        // =====================================================================

        private static void CreateTables()
        {
            // ---- Paramétrage -------------------------------------------------
            Table(Db.Setup, "Maintenance - Paramètres", BoUTBTableType.bott_NoObject);
            string t = "@" + Db.Setup;
            Fld(t, "ExpAcct", "Cpte charges maintenance", 'A', 15);
            Fld(t, "LabExpAcct", "Cpte charges main-d'oeuvre", 'A', 15);
            Fld(t, "LabAbsAcct", "Cpte imputation main-d'oeuvre", 'A', 15);
            Fld(t, "PostLab", "Comptabiliser MO (Y/N)", 'A', 1);
            Fld(t, "DimCode", "Axe analytique (1-5)", 'N', 1);
            Fld(t, "DfltWhs", "Magasin par défaut", 'A', 8);
            Fld(t, "EqPrefix", "Préfixe code équipement", 'A', 5);
            Fld(t, "P1Hours", "Délai priorité 1 (h)", 'N', 6);
            Fld(t, "P2Hours", "Délai priorité 2 (h)", 'N', 6);
            Fld(t, "P3Hours", "Délai priorité 3 (h)", 'N', 6);
            Fld(t, "P4Hours", "Délai priorité 4 (h)", 'N', 6);
            Fld(t, "Horizon", "Horizon ordonnancement (j)", 'N', 4);
            Fld(t, "CapAcct", "Cpte immobilisations en cours", 'A', 15);

            // ---- Postes techniques (IL01) -----------------------------------
            Table(Db.FuncLoc, "Maint. - Postes techniques", BoUTBTableType.bott_MasterData);
            t = "@" + Db.FuncLoc;
            Fld(t, "Parent", "Poste technique supérieur", 'A', 50);
            Fld(t, "Location", "Emplacement / adresse", 'A', 100);
            Fld(t, "OcrCode", "Centre de coûts", 'A', 8);
            Fld(t, "Whs", "Magasin", 'A', 8);
            Fld(t, "Active", "Actif (Y/N)", 'A', 1);
            Fld(t, "Remarks", "Remarques", 'M');

            // ---- Catégories d'équipement ------------------------------------
            Table(Db.EqCat, "Maint. - Catégories équipement", BoUTBTableType.bott_MasterData);
            t = "@" + Db.EqCat;
            Fld(t, "ExpAcct", "Cpte charges (option)", 'A', 15);

            // ---- Postes de travail (IR01) -----------------------------------
            Table(Db.WorkCtr, "Maint. - Postes de travail", BoUTBTableType.bott_MasterData);
            t = "@" + Db.WorkCtr;
            Fld(t, "Type", "Type", 'A', 1, null, WorkCenterTypes.List);
            Fld(t, "Rate", "Taux horaire", 'P');
            Fld(t, "Capacity", "Capacité (h/jour)", 'Q');
            Fld(t, "AbsAcct", "Cpte imputation MO (option)", 'A', 15);
            Fld(t, "OcrCode", "Centre de coûts", 'A', 8);
            Fld(t, "Active", "Actif", 'A', 1, "Y", new CodeList("Y", "Oui", "N", "Non"));

            // ---- Catalogues (QS41) ------------------------------------------
            Table(Db.Catalog, "Maint. - Catalogues", BoUTBTableType.bott_MasterData);
            t = "@" + Db.Catalog;
            Fld(t, "Type", "Type de code", 'A', 1, null, CatalogTypes.List);
            Fld(t, "Active", "Actif", 'A', 1, "Y", new CodeList("Y", "Oui", "N", "Non"));

            // ---- Équipements (IE01) -----------------------------------------
            Table(Db.Equip, "Maint. - Équipements", BoUTBTableType.bott_MasterData);
            t = "@" + Db.Equip;
            Fld(t, "Category", "Catégorie", 'A', 50);
            Fld(t, "FuncLoc", "Poste technique", 'A', 50);
            Fld(t, "Parent", "Équipement supérieur", 'A', 50);
            Fld(t, "Status", "Statut (A/I/S)", 'A', 1);
            Fld(t, "Critic", "Criticité (A/B/C)", 'A', 1);
            Fld(t, "Manuf", "Fabricant", 'A', 50);
            Fld(t, "Model", "Modèle", 'A', 50);
            Fld(t, "SerialNo", "N° de série", 'A', 50);
            Fld(t, "ConstYear", "Année de construction", 'N', 4);
            Fld(t, "ItemCode", "Article / immobilisation", 'A', 50);
            Fld(t, "Vendor", "Fournisseur", 'A', 15);
            Fld(t, "AcqDate", "Date d'acquisition", 'D');
            Fld(t, "AcqValue", "Valeur d'acquisition", 'S');
            Fld(t, "WarrEnd", "Fin de garantie", 'D');
            Fld(t, "StartUp", "Date de mise en service", 'D');
            Fld(t, "WorkCtr", "Poste de travail", 'A', 50);
            Fld(t, "OcrCode", "Centre de coûts", 'A', 8);
            Fld(t, "Whs", "Magasin pièces", 'A', 8);
            Fld(t, "Remarks", "Remarques", 'M');
            Fld(t, "MntVend", "Prestataire de maintenance", 'A', 15);
            Fld(t, "WarrVend", "Garant (fournisseur garantie)", 'A', 15);

            Table(Db.EquipPts, "Maint. - Points de mesure", BoUTBTableType.bott_MasterDataLines);
            t = "@" + Db.EquipPts;
            Fld(t, "Point", "Point de mesure", 'A', 20);
            Fld(t, "Descr", "Description", 'A', 100);
            Fld(t, "Unit", "Unité", 'A', 10);
            Fld(t, "Counter", "Compteur (Y/N)", 'A', 1);
            Fld(t, "AnnEst", "Estimation annuelle", 'F');
            Fld(t, "MinVal", "Limite basse", 'F');
            Fld(t, "MaxVal", "Limite haute", 'F');

            // ---- Gammes (IA05) ----------------------------------------------
            Table(Db.TaskList, "Maint. - Gammes", BoUTBTableType.bott_MasterData);
            t = "@" + Db.TaskList;
            Fld(t, "Category", "Catégorie d'équipement", 'A', 50);
            Fld(t, "Equip", "Équipement", 'A', 50);
            Fld(t, "WorkCtr", "Poste de travail", 'A', 50);
            Fld(t, "OrdType", "Type d'ordre", 'A', 4);
            Fld(t, "Active", "Active (Y/N)", 'A', 1);
            Fld(t, "Remarks", "Remarques", 'M');

            Table(Db.TaskOps, "Maint. - Opérations de gamme", BoUTBTableType.bott_MasterDataLines);
            OperationFields("@" + Db.TaskOps, false);

            Table(Db.TaskComps, "Maint. - Composants de gamme", BoUTBTableType.bott_MasterDataLines);
            ComponentFields("@" + Db.TaskComps, false);

            // ---- Plans de maintenance (IP41/IP42) ---------------------------
            Table(Db.Plan, "Maint. - Plans de maintenance", BoUTBTableType.bott_MasterData);
            t = "@" + Db.Plan;
            Fld(t, "Type", "Type (T temps / C compteur)", 'A', 1);
            Fld(t, "Equip", "Équipement", 'A', 50);
            Fld(t, "FuncLoc", "Poste technique", 'A', 50);
            Fld(t, "TaskList", "Gamme", 'A', 50);
            Fld(t, "OrdType", "Type d'ordre", 'A', 4);
            Fld(t, "Priority", "Priorité", 'A', 1);
            Fld(t, "WorkCtr", "Poste de travail", 'A', 50);
            Fld(t, "Cycle", "Cycle (temps)", 'N', 6);
            Fld(t, "CycUnit", "Unité du cycle", 'A', 1);
            Fld(t, "Point", "Point de mesure", 'A', 20);
            Fld(t, "CycCount", "Cycle (compteur)", 'F');
            Fld(t, "LeadDays", "Horizon d'appel (jours)", 'N', 6);
            Fld(t, "LeadCnt", "Horizon d'appel (compteur)", 'F');
            Fld(t, "Basis", "Base d'ordonnancement", 'A', 1);
            Fld(t, "StartDate", "Date de début du cycle", 'D');
            Fld(t, "StartCnt", "Compteur de début", 'F');
            Fld(t, "NextDate", "Prochaine échéance", 'D');
            Fld(t, "NextCnt", "Prochaine échéance compteur", 'F');
            Fld(t, "LastDate", "Dernière réalisation", 'D');
            Fld(t, "LastCnt", "Dernier compteur réalisé", 'F');
            Fld(t, "Active", "Actif (Y/N)", 'A', 1);
            Fld(t, "Remarks", "Remarques", 'M');

            // ---- Avis (IW21) -------------------------------------------------
            Table(Db.Notif, "Maint. - Avis", BoUTBTableType.bott_Document);
            t = "@" + Db.Notif;
            Fld(t, "Type", "Type d'avis", 'A', 2);
            Fld(t, "Status", "Statut", 'A', 4);
            Fld(t, "Equip", "Équipement", 'A', 50);
            Fld(t, "FuncLoc", "Poste technique", 'A', 50);
            Fld(t, "Priority", "Priorité", 'A', 1);
            Fld(t, "Subject", "Description courte", 'A', 100);
            Fld(t, "Descr", "Description détaillée", 'M');
            Fld(t, "ReportBy", "Déclaré par", 'A', 50);
            Fld(t, "RepDate", "Date de déclaration", 'D');
            Fld(t, "RepTime", "Heure de déclaration", 'H');
            Fld(t, "ReqStart", "Début souhaité", 'D');
            Fld(t, "ReqEnd", "Fin souhaitée", 'D');
            Fld(t, "WorkCtr", "Poste de travail", 'A', 50);
            Fld(t, "Breakdwn", "Arrêt machine (Y/N)", 'A', 1);
            Fld(t, "MalfStD", "Début de panne (date)", 'D');
            Fld(t, "MalfStT", "Début de panne (heure)", 'H');
            Fld(t, "MalfEnD", "Fin de panne (date)", 'D');
            Fld(t, "MalfEnT", "Fin de panne (heure)", 'H');
            Fld(t, "ObjPart", "Partie d'objet", 'A', 50);
            Fld(t, "Damage", "Dommage", 'A', 50);
            Fld(t, "Cause", "Cause", 'A', 50);
            Fld(t, "Activity", "Activité", 'A', 50);
            Fld(t, "OrderNo", "Ordre (DocEntry)", 'N', 11);
            Fld(t, "ComplDt", "Date de fin d'avis", 'D');
            Fld(t, "Point", "Point de mesure", 'A', 20);
            Fld(t, "UnderWar", "Sous garantie (Y/N)", 'A', 1);
            Fld(t, "Contract", "Contrat de maintenance", 'A', 50);

            // ---- Ordres (IW31) -----------------------------------------------
            Table(Db.Order, "Maint. - Ordres", BoUTBTableType.bott_Document);
            t = "@" + Db.Order;
            Fld(t, "OrdType", "Type d'ordre", 'A', 4);
            Fld(t, "Status", "Statut système", 'A', 4);
            Fld(t, "Equip", "Équipement", 'A', 50);
            Fld(t, "FuncLoc", "Poste technique", 'A', 50);
            Fld(t, "NotifNo", "Avis (DocEntry)", 'N', 11);
            Fld(t, "PlanCode", "Plan de maintenance", 'A', 50);
            Fld(t, "CallDue", "Échéance du plan", 'D');
            Fld(t, "TaskList", "Gamme reprise", 'A', 50);
            Fld(t, "Priority", "Priorité", 'A', 1);
            Fld(t, "Subject", "Description courte", 'A', 100);
            Fld(t, "Descr", "Description détaillée", 'M');
            Fld(t, "WorkCtr", "Poste de travail principal", 'A', 50);
            Fld(t, "Respons", "Responsable", 'A', 50);
            Fld(t, "OcrCode", "Centre de coûts", 'A', 8);
            Fld(t, "StartDt", "Début planifié", 'D');
            Fld(t, "EndDt", "Fin planifiée", 'D');
            Fld(t, "ActStart", "Début réel", 'D');
            Fld(t, "ActEnd", "Fin réelle", 'D');
            Fld(t, "RelDate", "Date de lancement", 'D');
            Fld(t, "TecoDate", "Date clôture technique", 'D');
            Fld(t, "CloseDt", "Date de clôture", 'D');
            Fld(t, "PlLab", "Prévu main-d'oeuvre", 'S');
            Fld(t, "PlMat", "Prévu matières", 'S');
            Fld(t, "PlExt", "Prévu prestations", 'S');
            Fld(t, "AcLab", "Réel main-d'oeuvre", 'S');
            Fld(t, "AcMat", "Réel matières", 'S');
            Fld(t, "AcExt", "Réel prestations", 'S');
            Fld(t, "UnderWar", "Sous garantie (Y/N)", 'A', 1);
            Fld(t, "Contract", "Contrat de maintenance", 'A', 50);
            Fld(t, "Capital", "À immobiliser (Y/N)", 'A', 1);
            Fld(t, "CapAcct", "Compte de règlement", 'A', 15);
            Fld(t, "SettJE", "Écriture de règlement", 'N', 11);
            Fld(t, "SettAmt", "Montant réglé", 'S');

            Table(Db.OrderOps, "Maint. - Opérations d'ordre", BoUTBTableType.bott_DocumentLines);
            OperationFields("@" + Db.OrderOps, true);

            Table(Db.OrderComps, "Maint. - Composants d'ordre", BoUTBTableType.bott_DocumentLines);
            ComponentFields("@" + Db.OrderComps, true);

            // ---- Confirmations (IW41) ---------------------------------------
            Table(Db.Conf, "Maint. - Confirmations", BoUTBTableType.bott_NoObject);
            t = "@" + Db.Conf;
            Fld(t, "OrderNo", "Ordre (DocEntry)", 'N', 11);
            Fld(t, "OpLine", "Ligne d'opération", 'N', 6);
            Fld(t, "OpNo", "N° d'opération", 'A', 4);
            Fld(t, "ConfDate", "Date", 'D');
            Fld(t, "EmpId", "Salarié (empID)", 'N', 11);
            Fld(t, "EmpName", "Salarié", 'A', 100);
            Fld(t, "WorkCtr", "Poste de travail", 'A', 50);
            Fld(t, "Hours", "Heures", 'Q');
            Fld(t, "Rate", "Taux horaire", 'P');
            Fld(t, "Amount", "Montant", 'S');
            Fld(t, "Final", "Confirmation finale (Y/N)", 'A', 1);
            Fld(t, "Remarks", "Commentaire", 'A', 200);
            Fld(t, "TransId", "N° écriture", 'N', 11);
            Fld(t, "User", "Utilisateur SAP", 'A', 25);

            // ---- Documents de mesure (IK11) ---------------------------------
            Table(Db.MeasDoc, "Maint. - Documents de mesure", BoUTBTableType.bott_NoObject);
            t = "@" + Db.MeasDoc;
            Fld(t, "Equip", "Équipement", 'A', 50);
            Fld(t, "Point", "Point de mesure", 'A', 20);
            Fld(t, "MDate", "Date", 'D');
            Fld(t, "MTime", "Heure", 'H');
            Fld(t, "Value", "Valeur / relevé", 'F');
            Fld(t, "Diff", "Écart avec relevé précédent", 'F');
            Fld(t, "Remarks", "Commentaire", 'A', 200);
            Fld(t, "User", "Utilisateur SAP", 'A', 25);
            Fld(t, "NotifNo", "Avis créé (DocEntry)", 'N', 11);

            // ---- Appels des plans (IP30) ------------------------------------
            Table(Db.Call, "Maint. - Appels de plans", BoUTBTableType.bott_NoObject);
            t = "@" + Db.Call;
            Fld(t, "PlanCode", "Plan de maintenance", 'A', 50);
            Fld(t, "DueDate", "Échéance (date)", 'D');
            Fld(t, "DueCnt", "Échéance (compteur)", 'F');
            Fld(t, "CallDate", "Date d'appel", 'D');
            Fld(t, "OrderNo", "Ordre (DocEntry)", 'N', 11);
            Fld(t, "Status", "Statut (O/D/S)", 'A', 1);
            Fld(t, "DoneDate", "Date de réalisation", 'D');
            Fld(t, "User", "Utilisateur SAP", 'A', 25);

            // ---- Contrats de maintenance ------------------------------------
            Table(Db.Contract, "Maint. - Contrats", BoUTBTableType.bott_MasterData);
            t = "@" + Db.Contract;
            Fld(t, "Vendor", "Prestataire", 'A', 15);
            Fld(t, "RefExt", "Référence prestataire", 'A', 50);
            Fld(t, "Type", "Type (P/C/D)", 'A', 1);
            Fld(t, "StartDt", "Début", 'D');
            Fld(t, "EndDt", "Fin", 'D');
            Fld(t, "Notice", "Préavis de résiliation (j)", 'N', 4);
            Fld(t, "Amount", "Montant annuel", 'S');
            Fld(t, "Billing", "Périodicité de facturation", 'A', 1);
            Fld(t, "RespHrs", "Délai d'intervention (h)", 'N', 6);
            Fld(t, "Visits", "Visites préventives / an", 'N', 4);
            Fld(t, "CovLab", "Main-d'oeuvre incluse (Y/N)", 'A', 1);
            Fld(t, "CovParts", "Pièces incluses (Y/N)", 'A', 1);
            Fld(t, "Active", "Actif (Y/N)", 'A', 1);
            Fld(t, "Remarks", "Remarques", 'M');

            Table(Db.ContractEq, "Maint. - Équipements contrat", BoUTBTableType.bott_MasterDataLines);
            t = "@" + Db.ContractEq;
            Fld(t, "Equip", "Équipement", 'A', 50);
            Fld(t, "EqName", "Désignation", 'A', 100);

            // ---- Envois chez un prestataire ---------------------------------
            Table(Db.Ship, "Maint. - Envois prestataires", BoUTBTableType.bott_NoObject);
            t = "@" + Db.Ship;
            Fld(t, "Equip", "Équipement", 'A', 50);
            Fld(t, "Vendor", "Prestataire", 'A', 15);
            Fld(t, "OrderNo", "Ordre (DocEntry)", 'N', 11);
            Fld(t, "SentDate", "Date d'envoi", 'D');
            Fld(t, "ExpRet", "Retour prévu", 'D');
            Fld(t, "RetDate", "Date de retour", 'D');
            Fld(t, "Status", "Statut (O/R)", 'A', 1);
            Fld(t, "PrevSt", "Statut équipement avant", 'A', 1);
            Fld(t, "Reason", "Motif de l'envoi", 'A', 200);
            Fld(t, "RetNote", "Commentaire de retour", 'A', 200);
            Fld(t, "User", "Utilisateur SAP", 'A', 25);
        }

        private static void OperationFields(string t, bool onOrder)
        {
            Fld(t, "OpNo", "N° d'opération", 'A', 4);
            Fld(t, "Descr", "Description", 'A', 100);
            Fld(t, "WorkCtr", "Poste de travail", 'A', 50);
            Fld(t, "CtrlKey", "Clé (INT/EXT)", 'A', 3);
            Fld(t, "PlanHrs", "Travail prévu (h)", 'Q');
            Fld(t, "NbPers", "Nombre de personnes", 'N', 4);
            Fld(t, "ExtCost", "Coût prestation prévu", 'S');
            Fld(t, "Vendor", "Prestataire", 'A', 15);
            if (!onOrder)
                return;
            Fld(t, "ActHrs", "Travail réel (h)", 'Q');
            Fld(t, "Done", "Terminée (Y/N)", 'A', 1);
            Fld(t, "PrEntry", "Demande d'achat (DocEntry)", 'N', 11);
        }

        private static void ComponentFields(string t, bool onOrder)
        {
            Fld(t, "ItemCode", "Article", 'A', 50);
            Fld(t, "ItemName", "Désignation", 'A', 100);
            Fld(t, "Qty", "Quantité prévue", 'Q');
            Fld(t, "Whs", "Magasin", 'A', 8);
            Fld(t, "OpNo", "Opération", 'A', 4);
            Fld(t, "Proc", "Approvisionnement (S/P)", 'A', 1);
            if (!onOrder)
                return;
            Fld(t, "UnitCost", "Coût unitaire prévu", 'P');
            Fld(t, "IssQty", "Quantité sortie", 'Q');
            Fld(t, "PrEntry", "Demande d'achat (DocEntry)", 'N', 11);
        }

        /// <summary>Imputation des documents SAP (sorties, entrées, achats) sur un ordre.</summary>
        private static void CreateDocumentFields()
        {
            // Champs de lignes des documents marketing : SAP les crée sur
            // toutes les tables de lignes (IGE1, IGN1, PRQ1, POR1, PDN1, PCH1, RPC1...).
            Fld("INV1", "MNT_Ord", "Ordre de maintenance", 'N', 11);
            Fld("INV1", "MNT_Line", "Ligne d'ordre maintenance", 'N', 6);
            Fld("INV1", "MNT_Ctr", "Contrat de maintenance", 'A', 50);
            // Contrôle : si la propagation n'a pas eu lieu, on crée sur les tables utilisées
            foreach (string table in new[] { "IGE1", "IGN1", "PRQ1", "POR1", "PCH1", "RPC1" })
            {
                Fld(table, "MNT_Ord", "Ordre de maintenance", 'N', 11);
                Fld(table, "MNT_Line", "Ligne d'ordre maintenance", 'N', 6);
                Fld(table, "MNT_Ctr", "Contrat de maintenance", 'A', 50);
            }
        }

        // =====================================================================
        // Objets UDO
        // =====================================================================

        private static void RegisterObjects()
        {
            // Paramétrage simple : fenêtres par défaut SAP (grille éditable)
            Register(Obj.EqCat, "Maint. - Catégories d'équipement", BoUDOObjType.boud_MasterData, Db.EqCat, null,
                new[] { "Code", "Code", "Name", "Désignation", "U_ExpAcct", "Compte de charges (option)" });
            Register(Obj.WorkCtr, "Maint. - Postes de travail", BoUDOObjType.boud_MasterData, Db.WorkCtr, null,
                new[] { "Code", "Code", "Name", "Désignation", "U_Type", "Type", "U_Rate", "Taux horaire",
                        "U_Capacity", "Capacité (h/j)", "U_AbsAcct", "Compte imputation MO", "U_OcrCode", "Centre de coûts", "U_Active", "Actif" });
            Register(Obj.Catalog, "Maint. - Catalogues", BoUDOObjType.boud_MasterData, Db.Catalog, null,
                new[] { "Code", "Code", "Name", "Libellé", "U_Type", "Type de code", "U_Active", "Actif" });

            // Objets avec écrans dédiés de l'add-on
            Register(Obj.FuncLoc, "Maint. - Postes techniques", BoUDOObjType.boud_MasterData, Db.FuncLoc, null, null);
            Register(Obj.Equip, "Maint. - Équipements", BoUDOObjType.boud_MasterData, Db.Equip, new[] { Db.EquipPts }, null);
            Register(Obj.TaskList, "Maint. - Gammes", BoUDOObjType.boud_MasterData, Db.TaskList, new[] { Db.TaskOps, Db.TaskComps }, null);
            Register(Obj.Plan, "Maint. - Plans de maintenance", BoUDOObjType.boud_MasterData, Db.Plan, null, null);
            Register(Obj.Contract, "Maint. - Contrats de maintenance", BoUDOObjType.boud_MasterData, Db.Contract, new[] { Db.ContractEq }, null);
            Register(Obj.Notif, "Maint. - Avis", BoUDOObjType.boud_Document, Db.Notif, null, null);
            Register(Obj.Order, "Maint. - Ordres", BoUDOObjType.boud_Document, Db.Order, new[] { Db.OrderOps, Db.OrderComps }, null);

            // SAP refuse de créer un document UDO sans série de numérotation
            EnsureSeries(Obj.Notif);
            EnsureSeries(Obj.Order);
        }

        /// <summary>Série « Primaire » (1, 2, 3...) par défaut pour tous les utilisateurs, si l'objet n'en a aucune.</summary>
        private static void EnsureSeries(string objectCode)
        {
            CompanyService cs = DiCompany.Instance.GetCompanyService();
            SeriesService ss = (SeriesService)cs.GetBusinessService(ServiceTypes.SeriesService);
            int series = (int)Sql.ScalarDbl("SELECT MIN(\"Series\") FROM \"NNM1\" WHERE \"ObjectCode\" = " + Sql.Q(objectCode));
            if (series == 0)
            {
                Report("Création de la série de numérotation de " + objectCode + "...");
                // Indicateur de période de la société (ex. « Valeur par défaut » en français)
                string indicator = Sql.ScalarStr("SELECT TOP 1 \"Indicator\" FROM \"NNM1\" WHERE \"ObjectCode\" = '17' ORDER BY \"Series\"");
                if (indicator == "")
                    indicator = Sql.ScalarStr("SELECT TOP 1 \"Indicator\" FROM \"OPID\"");

                Series s = (Series)ss.GetDataInterface(SeriesServiceDataInterfaces.ssdiSeries);
                s.Document = objectCode;
                s.Name = "Primaire";
                s.InitialNumber = 1;
                s.LastNumber = 999999999;
                if (indicator != "")
                    s.PeriodIndicator = indicator;
                SeriesParams p = ss.AddSeries(s);
                series = p.Series;
                _changed = true;
            }
            if (Sql.ScalarDbl("SELECT \"DfltSeries\" FROM \"ONNM\" WHERE \"ObjectCode\" = " + Sql.Q(objectCode)) == 0)
            {
                DocumentSeriesParams d = (DocumentSeriesParams)ss.GetDataInterface(SeriesServiceDataInterfaces.ssdiDocumentSeriesParams);
                d.Document = objectCode;
                d.Series = series;
                ss.SetDefaultSeriesForAllUsers(d);
                _changed = true;
            }
        }

        /// <param name="formColumns">Alias / libellé, par paires : crée la fenêtre par défaut SAP (grille).</param>
        private static void Register(string code, string name, BoUDOObjType type, string table, string[] children, string[] formColumns)
        {
            Company company = DiCompany.Instance;
            UserObjectsMD md = (UserObjectsMD)company.GetBusinessObject(BoObjectTypes.oUserObjectsMD);
            try
            {
                if (md.GetByKey(code))
                    return;

                Report("Enregistrement de l'objet " + code + "...");
                md.Code = code;
                md.Name = name;
                md.ObjectType = type;
                md.TableName = table;
                md.CanFind = BoYesNoEnum.tYES;
                md.CanLog = BoYesNoEnum.tNO;
                md.CanYearTransfer = BoYesNoEnum.tNO;
                md.CanCancel = BoYesNoEnum.tNO;
                md.CanClose = BoYesNoEnum.tNO;
                if (type == BoUDOObjType.boud_Document)
                {
                    // Un document ne se supprime pas : il s'annule par son statut
                    md.CanDelete = BoYesNoEnum.tNO;
                    md.ManageSeries = BoYesNoEnum.tYES;
                }
                else
                {
                    md.CanDelete = BoYesNoEnum.tYES;
                    md.ManageSeries = BoYesNoEnum.tNO;
                }

                if (children != null)
                {
                    for (int i = 0; i < children.Length; i++)
                    {
                        if (i > 0)
                            md.ChildTables.Add();
                        md.ChildTables.TableName = children[i];
                    }
                }

                // Colonnes de recherche (fenêtre "Rechercher")
                string[] find = type == BoUDOObjType.boud_Document
                    ? new[] { "DocEntry", "N° interne", "DocNum", "N°" }
                    : new[] { "Code", "Code", "Name", "Désignation" };
                for (int i = 0; i < find.Length; i += 2)
                {
                    if (i > 0)
                        md.FindColumns.Add();
                    md.FindColumns.ColumnAlias = find[i];
                    md.FindColumns.ColumnDescription = find[i + 1];
                }

                if (formColumns != null)
                {
                    md.CanCreateDefaultForm = BoYesNoEnum.tYES;
                    md.EnableEnhancedForm = BoYesNoEnum.tNO;
                    for (int i = 0; i < formColumns.Length; i += 2)
                    {
                        if (i > 0)
                            md.FormColumns.Add();
                        md.FormColumns.FormColumnAlias = formColumns[i];
                        md.FormColumns.FormColumnDescription = formColumns[i + 1];
                        md.FormColumns.Editable = formColumns[i] == "Code" ? BoYesNoEnum.tNO : BoYesNoEnum.tYES;
                    }
                }
                else
                {
                    md.CanCreateDefaultForm = BoYesNoEnum.tNO;
                }

                DiCompany.ThrowIfError(md.Add(), "Enregistrement de l'objet " + code);
                _changed = true;
            }
            finally
            {
                Marshal.ReleaseComObject(md);
            }
        }

        // =====================================================================
        // Paramétrage et données de départ
        // =====================================================================

        private static void EnsureSetupRow()
        {
            UserTable table = DiCompany.Instance.UserTables.Item(Db.Setup);
            try
            {
                if (table.GetByKey(Db.SetupCode))
                    return;

                table.Code = Db.SetupCode;
                table.Name = Db.SetupCode;
                table.UserFields.Fields.Item("U_PostLab").Value = "N";
                table.UserFields.Fields.Item("U_DimCode").Value = 1;
                table.UserFields.Fields.Item("U_EqPrefix").Value = "EQ";
                table.UserFields.Fields.Item("U_P1Hours").Value = 4;
                table.UserFields.Fields.Item("U_P2Hours").Value = 24;
                table.UserFields.Fields.Item("U_P3Hours").Value = 72;
                table.UserFields.Fields.Item("U_P4Hours").Value = 168;
                table.UserFields.Fields.Item("U_Horizon").Value = 30;
                DiCompany.ThrowIfError(table.Add(), "Initialisation du paramétrage maintenance");
            }
            finally
            {
                Marshal.ReleaseComObject(table);
            }
        }

        /// <summary>Catalogues, catégories et postes de travail d'exemple (tables vides seulement).</summary>
        private static void SeedData()
        {
            if (IsEmpty(Db.Catalog))
            {
                Report("Création des catalogues de départ...");
                var codes = new[]
                {
                    // Parties d'objet
                    "O-MOT", "Moteur", "O", "O-ELE", "Partie électrique", "O", "O-HYD", "Circuit hydraulique", "O",
                    "O-MEC", "Transmission / mécanique", "O", "O-STR", "Structure / carrosserie", "O", "O-PNE", "Pneumatiques", "O",
                    // Dommages
                    "D-FUI", "Fuite", "D", "D-USU", "Usure", "D", "D-CAS", "Casse", "D", "D-SUR", "Surchauffe", "D",
                    "D-BRU", "Bruit / vibration anormale", "D", "D-ELE", "Défaut électrique", "D",
                    // Causes
                    "C-USN", "Usure normale", "C", "C-MAU", "Mauvaise utilisation", "C", "C-ENT", "Défaut d'entretien", "C",
                    "C-PIE", "Pièce défectueuse", "C", "C-ACC", "Accident / choc", "C",
                    // Activités
                    "A-REM", "Remplacement de pièce", "A", "A-REG", "Réglage", "A", "A-NET", "Nettoyage", "A",
                    "A-GRA", "Graissage / lubrification", "A", "A-CTL", "Contrôle / inspection", "A", "A-REP", "Réparation", "A"
                };
                for (int i = 0; i < codes.Length; i += 3)
                    AddMasterData(Obj.Catalog, codes[i], codes[i + 1], new Dictionary<string, object> { { "U_Type", codes[i + 2] }, { "U_Active", "Y" } });
            }

            if (IsEmpty(Db.EqCat))
            {
                foreach (var c in new[] { "MACH", "Machines de production", "VEHI", "Véhicules et engins", "BATI", "Bâtiments et installations",
                                          "ELEC", "Équipements électriques", "INFO", "Informatique et réseaux" }.Pairs())
                    AddMasterData(Obj.EqCat, c.Key, c.Value, null);
            }

            if (IsEmpty(Db.WorkCtr))
            {
                AddMasterData(Obj.WorkCtr, "MECA", "Atelier mécanique", new Dictionary<string, object> { { "U_Type", "I" }, { "U_Rate", 0.0 }, { "U_Capacity", 8.0 }, { "U_Active", "Y" } });
                AddMasterData(Obj.WorkCtr, "ELEC", "Atelier électrique", new Dictionary<string, object> { { "U_Type", "I" }, { "U_Rate", 0.0 }, { "U_Capacity", 8.0 }, { "U_Active", "Y" } });
                AddMasterData(Obj.WorkCtr, "EXT", "Prestataires externes", new Dictionary<string, object> { { "U_Type", "E" }, { "U_Rate", 0.0 }, { "U_Capacity", 0.0 }, { "U_Active", "Y" } });
            }
        }

        private static IEnumerable<KeyValuePair<string, string>> Pairs(this string[] values)
        {
            for (int i = 0; i + 1 < values.Length; i += 2)
                yield return new KeyValuePair<string, string>(values[i], values[i + 1]);
        }

        private static bool IsEmpty(string table)
        {
            return !Sql.Exists("SELECT TOP 1 1 FROM " + Db.T(table));
        }

        private static void AddMasterData(string objectCode, string code, string name, Dictionary<string, object> fields)
        {
            UdoData d = UdoData.New(objectCode);
            d.Set("Code", code);
            d.Set("Name", name);
            if (fields != null)
                foreach (var f in fields)
                    d.Set(f.Key, f.Value);
            d.Add();
            _changed = true;
        }

        // =====================================================================
        // Outils de création
        // =====================================================================

        private static void Report(string message)
        {
            Program.Log(message);
            Progress?.Invoke(message);
        }

        private static void Table(string tableName, string description, BoUTBTableType type)
        {
            Company company = DiCompany.Instance;
            UserTablesMD md = (UserTablesMD)company.GetBusinessObject(BoObjectTypes.oUserTables);
            try
            {
                if (md.GetByKey(tableName))
                    return;

                Report("Création de la table @" + tableName + "...");
                md.TableName = tableName;
                md.TableDescription = description.Length > 30 ? description.Substring(0, 30) : description;
                md.TableType = type;
                DiCompany.ThrowIfError(md.Add(), "Création de la table @" + tableName);
                _changed = true;
            }
            finally
            {
                Marshal.ReleaseComObject(md);
            }
        }

        /// <summary>
        /// Crée un champ s'il n'existe pas. Types : A alpha, M mémo, N entier,
        /// Q quantité, S montant, P prix, F mesure, D date, H heure.
        /// </summary>
        private static void Fld(string tableId, string name, string description, char kind, int size = 0,
                                string defaultValue = null, CodeList validValues = null)
        {
            if (Sql.Exists("SELECT 1 FROM \"CUFD\" WHERE \"TableID\" = " + Sql.Q(tableId) + " AND \"AliasID\" = " + Sql.Q(name)))
                return;

            Company company = DiCompany.Instance;
            UserFieldsMD md = (UserFieldsMD)company.GetBusinessObject(BoObjectTypes.oUserFields);
            try
            {
                md.TableName = tableId;
                md.Name = name;
                md.Description = description.Length > 30 ? description.Substring(0, 30) : description;
                switch (kind)
                {
                    case 'A': md.Type = BoFieldTypes.db_Alpha; md.EditSize = size; break;
                    case 'M': md.Type = BoFieldTypes.db_Memo; break;
                    case 'N': md.Type = BoFieldTypes.db_Numeric; md.EditSize = size; break;
                    case 'Q': md.Type = BoFieldTypes.db_Float; md.SubType = BoFldSubTypes.st_Quantity; break;
                    case 'S': md.Type = BoFieldTypes.db_Float; md.SubType = BoFldSubTypes.st_Sum; break;
                    case 'P': md.Type = BoFieldTypes.db_Float; md.SubType = BoFldSubTypes.st_Price; break;
                    case 'F': md.Type = BoFieldTypes.db_Float; md.SubType = BoFldSubTypes.st_Measurement; break;
                    case 'D': md.Type = BoFieldTypes.db_Date; break;
                    case 'H': md.Type = BoFieldTypes.db_Date; md.SubType = BoFldSubTypes.st_Time; break;
                    default: throw new ArgumentException("Type de champ inconnu : " + kind);
                }

                if (validValues != null)
                {
                    bool first = true;
                    foreach (var v in validValues.Items)
                    {
                        if (!first)
                            md.ValidValues.Add();
                        md.ValidValues.Value = v.Key;
                        md.ValidValues.Description = v.Value;
                        first = false;
                    }
                }
                if (defaultValue != null)
                    md.DefaultValue = defaultValue;

                Report("Création du champ " + tableId + ".U_" + name + "...");
                DiCompany.ThrowIfError(md.Add(), "Création du champ U_" + name + " sur " + tableId);
                _changed = true;
            }
            finally
            {
                Marshal.ReleaseComObject(md);
            }
        }
    }
}
