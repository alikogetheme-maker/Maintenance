using System;
using SAPbobsCOM;

namespace MaintenanceAddon.Core
{
    /// <summary>
    /// Connecte le DI API en réutilisant la session déjà ouverte par le
    /// client SAP B1 (via le "cookie" de contexte), sans redemander les
    /// identifiants à l'utilisateur.
    /// </summary>
    internal static class DiCompany
    {
        private static Company _company;

        public static Company Instance
        {
            get
            {
                if (_company == null)
                    throw new InvalidOperationException("DiCompany n'est pas encore connecté. Appeler Connect() au démarrage.");
                return _company;
            }
        }

        /// <summary>Permet aux bancs de test de fournir une connexion DI directe.</summary>
        public static void Use(Company company)
        {
            _company = company;
        }

        public static void Connect(SAPbouiCOM.Application sboApplication)
        {
            _company = new Company();

            string cookie = _company.GetContextCookie();
            string connectionContext = sboApplication.Company.GetConnectionContext(cookie);

            int rc = _company.SetSboLoginContext(connectionContext);
            if (rc != 0)
                throw new InvalidOperationException("SetSboLoginContext a échoué (code " + rc + ").");

            if (!_company.Connected)
            {
                int connectRc = _company.Connect();
                if (connectRc != 0)
                {
                    _company.GetLastError(out int errCode, out string errMsg);
                    throw new InvalidOperationException("Connexion DI API échouée : " + errCode + " - " + errMsg);
                }
            }
        }

        public static void Disconnect()
        {
            try
            {
                if (_company != null && _company.Connected)
                    _company.Disconnect();
            }
            catch
            {
                // arrêt de l'add-on : on ignore
            }
        }

        public static void ThrowIfError(int returnCode, string context)
        {
            if (returnCode != 0)
            {
                Instance.GetLastError(out int errCode, out string errMsg);
                throw new InvalidOperationException(context + " : [" + errCode + "] " + errMsg);
            }
        }

        /// <summary>Code de l'utilisateur SAP connecté (OUSR.USER_CODE).</summary>
        public static string UserCode => Instance.UserName;

        /// <summary>
        /// Exécute une action dans une transaction DI API : tout est validé
        /// ou rien ne l'est. Imbrication tolérée (la transaction englobante décide).
        /// </summary>
        public static T InTransaction<T>(Func<T> action)
        {
            Company c = Instance;
            if (c.InTransaction)
                return action();

            c.StartTransaction();
            try
            {
                T result = action();
                c.EndTransaction(BoWfTransOpt.wf_Commit);
                return result;
            }
            catch
            {
                try
                {
                    if (c.InTransaction)
                        c.EndTransaction(BoWfTransOpt.wf_RollBack);
                }
                catch
                {
                    // l'erreur d'origine est plus utile que celle du rollback
                }
                throw;
            }
        }

        public static void InTransaction(Action action)
        {
            InTransaction(() => { action(); return 0; });
        }
    }
}
