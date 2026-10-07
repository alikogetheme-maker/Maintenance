using System;
using MaintenanceAddon.Core;

namespace MaintenanceAddon.Services
{
    /// <summary>
    /// Autorisations de l'add-on, créées dans l'arbre des autorisations SAP
    /// (Gestion → Initialisation système → Autorisations → Autorisations générales,
    /// rubrique « Autorisations utilisateur → Maintenance »).
    /// </summary>
    internal static class Perm
    {
        public const string Root = "MNT_AUTH";
        /// <summary>Postes techniques, équipements, gammes, plans, contrats, documents joints.</summary>
        public const string MasterData = "MNT_AUTH_MD";
        /// <summary>Avis : déclarer, terminer, rouvrir.</summary>
        public const string Notif = "MNT_AUTH_NOT";
        /// <summary>Ordres : créer, modifier, lancer, demandes d'achat, appels de plans.</summary>
        public const string Order = "MNT_AUTH_ORD";
        /// <summary>Exécution : confirmations de temps, sorties / retours de pièces, relevés, envois.</summary>
        public const string Exec = "MNT_AUTH_EXE";
        /// <summary>Clôture technique, clôture, annulation des ordres.</summary>
        public const string Close = "MNT_AUTH_CLO";
        public const string Setup = "MNT_AUTH_SET";
    }

    internal enum Access
    {
        None = 0,
        Read = 1,
        Full = 2
    }

    internal static class AuthService
    {
        /// <summary>Niveau de l'utilisateur (connecté par défaut) ; un super-utilisateur a tous les droits.</summary>
        public static Access Level(string permission, string userCode = null)
        {
            Row u = Sql.First("SELECT \"USERID\", \"SUPERUSER\" FROM \"OUSR\" WHERE \"USER_CODE\" = " + Sql.Q(userCode ?? DiCompany.UserCode));
            if (u == null)
                return Access.None;
            if (u.Str("SUPERUSER") == "Y")
                return Access.Full;
            switch (Sql.ScalarStr("SELECT \"Permission\" FROM \"USR3\" WHERE \"UserLink\" = " + u.Int("USERID") + " AND \"PermId\" = " + Sql.Q(permission)))
            {
                case "F": return Access.Full;
                case "R": return Access.Read;
                default: return Access.None;
            }
        }

        public static string Name(string permission)
        {
            string name = Sql.ScalarStr("SELECT \"Name\" FROM \"OUPT\" WHERE \"AbsId\" = " + Sql.Q(permission));
            return name == "" ? permission : name;
        }

        /// <summary>Message de refus si l'utilisateur n'a pas le niveau demandé, sinon null.</summary>
        public static string Denied(string permission, Access needed, string action)
        {
            if (Level(permission) >= needed)
                return null;
            return "Vous n'êtes pas autorisé à " + action + " (autorisation « Maintenance → " + Name(permission) + " » : " +
                   (needed == Access.Full ? "complète" : "lecture seule") + " requise).\n" +
                   "Demandez-la à l'administrateur SAP : Gestion → Initialisation système → Autorisations → Autorisations générales.";
        }

        /// <summary>Autorisation complète exigée pour une opération qui modifie les données.</summary>
        public static void Require(string permission, string action)
        {
            string refusal = Denied(permission, Access.Full, action);
            if (refusal != null)
                throw new InvalidOperationException(refusal);
        }
    }
}
