namespace MaintenanceAddon
{
    /// <summary>
    /// Tables utilisateur de l'add-on (noms sans le préfixe @).
    /// Préfixe MNT_ ; noms ≤ 10 caractères.
    /// </summary>
    internal static class Db
    {
        // Paramétrage (table simple, une seule ligne "1")
        public const string Setup = "MNT_SETUP";
        public const string SetupCode = "1";

        // Données de base (objets UDO "données de base")
        public const string FuncLoc = "MNT_OFLOC";      // postes techniques
        public const string EqCat = "MNT_OECT";         // catégories d'équipement
        public const string WorkCtr = "MNT_OWCT";       // postes de travail
        public const string Catalog = "MNT_OCAT";       // catalogues (partie d'objet, dommage, cause, activité)
        public const string Equip = "MNT_OEQP";         // équipements
        public const string EquipPts = "MNT_EQP1";      //   points de mesure de l'équipement
        public const string EquipParts = "MNT_EQP2";    //   pièces de rechange de l'équipement (articles SAP)
        public const string TaskList = "MNT_OTSK";      // gammes
        public const string TaskOps = "MNT_TSK1";       //   opérations de gamme
        public const string TaskComps = "MNT_TSK2";     //   composants de gamme
        public const string Plan = "MNT_OPLN";          // plans de maintenance préventive
        public const string Contract = "MNT_OCTR";      // contrats de maintenance
        public const string ContractEq = "MNT_CTR1";    //   équipements couverts

        // Documents (objets UDO "document")
        public const string Notif = "MNT_ONOT";         // avis de maintenance
        public const string Order = "MNT_OORD";         // ordres de maintenance
        public const string OrderOps = "MNT_ORD1";      //   opérations de l'ordre
        public const string OrderComps = "MNT_ORD2";    //   composants de l'ordre

        // Tables de mouvements (tables simples, écrites uniquement par l'add-on)
        public const string Conf = "MNT_CONF";          // confirmations de temps
        public const string MeasDoc = "MNT_MDOC";       // documents de mesure / relevés de compteur
        public const string Call = "MNT_CALL";          // appels (échéances) des plans
        public const string Ship = "MNT_SHIP";          // envois d'équipements chez un prestataire

        /// <summary>Nom SQL d'une table utilisateur : "@MNT_OORD" entre guillemets.</summary>
        public static string T(string table)
        {
            return "\"@" + table + "\"";
        }
    }

    /// <summary>Codes des objets UDO (≤ 20 caractères).</summary>
    internal static class Obj
    {
        public const string FuncLoc = "MNT_FLOC";
        public const string EqCat = "MNT_EQCAT";
        public const string WorkCtr = "MNT_WCTR";
        public const string Catalog = "MNT_CATAL";
        public const string Equip = "MNT_EQUIP";
        public const string TaskList = "MNT_TASKL";
        public const string Plan = "MNT_PLAN";
        public const string Contract = "MNT_CONTR";
        public const string Notif = "MNT_NOTIF";
        public const string Order = "MNT_ORDER";
    }

    /// <summary>Champs ajoutés sur les lignes des documents SAP (sorties de stock, achats...).</summary>
    internal static class DocFields
    {
        /// <summary>DocEntry de l'ordre de maintenance imputé.</summary>
        public const string Order = "U_MNT_Ord";
        /// <summary>LineId de la ligne de l'ordre (composant ou opération).</summary>
        public const string Line = "U_MNT_Line";
        /// <summary>Code du contrat de maintenance facturé (factures de contrat).</summary>
        public const string Contract = "U_MNT_Ctr";
    }
}
