using System.Collections.Generic;
using System.Linq;

namespace MaintenanceAddon.Models
{
    /// <summary>Liste de codes avec libellés (combos, rapports).</summary>
    internal sealed class CodeList
    {
        private readonly List<KeyValuePair<string, string>> _items = new List<KeyValuePair<string, string>>();

        public CodeList(params string[] codeThenCaption)
        {
            for (int i = 0; i + 1 < codeThenCaption.Length; i += 2)
                _items.Add(new KeyValuePair<string, string>(codeThenCaption[i], codeThenCaption[i + 1]));
        }

        public IEnumerable<KeyValuePair<string, string>> Items => _items;

        public string Caption(string code)
        {
            var hit = _items.FirstOrDefault(i => i.Key == code);
            return hit.Key == null ? code : hit.Value;
        }

        /// <summary>Expression SQL CASE traduisant une colonne en libellé (rapports).</summary>
        public string SqlCase(string column)
        {
            string sql = "CASE " + column;
            foreach (var i in _items)
                sql += " WHEN '" + i.Key + "' THEN N'" + i.Value.Replace("'", "''") + "'";
            return sql + " ELSE " + column + " END";
        }
    }

    /// <summary>Statuts système de l'ordre (comme SAP PM : CRTD, REL, TECO, CLSD).</summary>
    internal static class OrderStatus
    {
        public const string Created = "CRTD";
        public const string Released = "REL";
        public const string TechCompleted = "TECO";
        public const string Closed = "CLSD";
        public const string Cancelled = "CANC";

        public static readonly CodeList List = new CodeList(
            Created, "Créé (CRTD)",
            Released, "Lancé (REL)",
            TechCompleted, "Clôt. technique (TECO)",
            Closed, "Clôturé (CLSD)",
            Cancelled, "Annulé");
    }

    /// <summary>Statuts de l'avis (OSNO en attente, NOPR en cours, NOCO terminé).</summary>
    internal static class NotifStatus
    {
        public const string Outstanding = "OSNO";
        public const string InProcess = "NOPR";
        public const string Completed = "NOCO";

        public static readonly CodeList List = new CodeList(
            Outstanding, "En attente (OSNO)",
            InProcess, "En cours (NOPR)",
            Completed, "Terminé (NOCO)");
    }

    internal static class OrderTypes
    {
        public const string Corrective = "PM01";
        public const string Preventive = "PM02";
        public const string Improvement = "PM03";
        public const string Inspection = "PM04";

        public static readonly CodeList List = new CodeList(
            Corrective, "PM01 - Correctif",
            Preventive, "PM02 - Préventif",
            Improvement, "PM03 - Amélioration",
            Inspection, "PM04 - Inspection");
    }

    internal static class NotifTypes
    {
        public const string Request = "M1";
        public const string Malfunction = "M2";
        public const string Activity = "M3";

        public static readonly CodeList List = new CodeList(
            Request, "M1 - Demande de maintenance",
            Malfunction, "M2 - Avis de panne",
            Activity, "M3 - Rapport d'activité");
    }

    internal static class Priorities
    {
        public static readonly CodeList List = new CodeList(
            "1", "1 - Très élevée",
            "2", "2 - Élevée",
            "3", "3 - Moyenne",
            "4", "4 - Faible");
    }

    internal static class EquipStatus
    {
        public const string Active = "A";
        public const string Inactive = "I";
        public const string AtVendor = "R";
        public const string Scrapped = "S";
        public static readonly CodeList List = new CodeList(
            Active, "En service",
            Inactive, "Hors service",
            AtVendor, "Chez le prestataire",
            Scrapped, "Mis au rebut");
    }

    /// <summary>Étendue d'un contrat de maintenance.</summary>
    internal static class ContractTypes
    {
        public static readonly CodeList List = new CodeList(
            "P", "Préventif (visites)",
            "C", "Complet (préventif + dépannages)",
            "D", "Dépannage uniquement");
    }

    internal static class BillingPeriods
    {
        public static readonly CodeList List = new CodeList(
            "M", "Mensuelle",
            "Q", "Trimestrielle",
            "S", "Semestrielle",
            "Y", "Annuelle");

        public static int PerYear(string code)
        {
            switch (code)
            {
                case "M": return 12;
                case "Q": return 4;
                case "S": return 2;
                default: return 1;
            }
        }
    }

    /// <summary>Envoi d'un équipement chez un prestataire (réparation en atelier externe).</summary>
    internal static class ShipStatus
    {
        public const string Out = "O";
        public const string Returned = "R";
        public static readonly CodeList List = new CodeList(
            Out, "Chez le prestataire",
            Returned, "Revenu");
    }

    internal static class Criticality
    {
        public static readonly CodeList List = new CodeList(
            "A", "A - Critique",
            "B", "B - Importante",
            "C", "C - Secondaire");
    }

    /// <summary>Clé de commande de l'opération : interne (temps confirmé) ou externe (sous-traitée).</summary>
    internal static class ControlKeys
    {
        public const string Internal = "INT";
        public const string External = "EXT";
        public static readonly CodeList List = new CodeList(
            Internal, "Interne",
            External, "Externe");
    }

    /// <summary>Approvisionnement du composant : sortie de stock ou demande d'achat.</summary>
    internal static class Procurement
    {
        public const string Stock = "S";
        public const string Purchase = "P";
        public static readonly CodeList List = new CodeList(
            Stock, "Stock",
            Purchase, "Achat");
    }

    internal static class PlanTypes
    {
        public const string Time = "T";
        public const string Counter = "C";
        public static readonly CodeList List = new CodeList(
            Time, "Temps (calendrier)",
            Counter, "Compteur");
    }

    internal static class CycleUnits
    {
        public static readonly CodeList List = new CodeList(
            "D", "Jour(s)",
            "W", "Semaine(s)",
            "M", "Mois",
            "Y", "An(s)");
    }

    /// <summary>Base de calcul de la prochaine échéance (indicateur d'ordonnancement SAP).</summary>
    internal static class SchedBasis
    {
        public const string Planned = "P";
        public const string Completion = "C";
        public static readonly CodeList List = new CodeList(
            Planned, "Date planifiée",
            Completion, "Date de clôture technique");
    }

    internal static class CallStatus
    {
        public const string Open = "O";
        public const string Done = "D";
        public const string Skipped = "S";
        public static readonly CodeList List = new CodeList(
            Open, "Ordre en cours",
            Done, "Réalisé",
            Skipped, "Ignoré");
    }

    internal static class CatalogTypes
    {
        public const string ObjectPart = "O";
        public const string Damage = "D";
        public const string Cause = "C";
        public const string Activity = "A";
        public static readonly CodeList List = new CodeList(
            ObjectPart, "Partie d'objet",
            Damage, "Dommage",
            Cause, "Cause",
            Activity, "Activité");
    }

    /// <summary>Statut de l'ordre de fabrication SAP (OWOR.Status).</summary>
    internal static class ProdStatus
    {
        public const string Planned = "P";
        public const string Released = "R";
        public const string Closed = "L";
        public const string Cancelled = "C";
        public static readonly CodeList List = new CodeList(
            Planned, "Planifié",
            Released, "Lancé",
            Closed, "Clôturé",
            Cancelled, "Annulé");
    }

    internal static class WorkCenterTypes
    {
        public static readonly CodeList List = new CodeList(
            "I", "Interne",
            "E", "Externe (prestataire)");
    }
}
