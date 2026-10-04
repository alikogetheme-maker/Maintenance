using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using SAPbobsCOM;

namespace MaintenanceAddon.Core
{
    /// <summary>
    /// Lecture / écriture des objets UDO de l'add-on via le GeneralService
    /// du DI API (participe aux transactions DI API).
    /// </summary>
    internal sealed class UdoData
    {
        private readonly GeneralService _service;
        public GeneralData Data { get; }

        private UdoData(GeneralService service, GeneralData data)
        {
            _service = service;
            Data = data;
        }

        private static GeneralService Service(string objectCode)
        {
            CompanyService cs = DiCompany.Instance.GetCompanyService();
            return (GeneralService)cs.GetGeneralService(objectCode);
        }

        public static UdoData New(string objectCode)
        {
            GeneralService gs = Service(objectCode);
            return new UdoData(gs, (GeneralData)gs.GetDataInterface(GeneralServiceDataInterfaces.gsGeneralData));
        }

        /// <summary>Document (DocEntry) ou donnée de base (Code).</summary>
        public static UdoData Get(string objectCode, object key)
        {
            GeneralService gs = Service(objectCode);
            GeneralDataParams p = (GeneralDataParams)gs.GetDataInterface(GeneralServiceDataInterfaces.gsGeneralDataParams);
            if (key is int i)
                p.SetProperty("DocEntry", i);
            else
                p.SetProperty("Code", Convert.ToString(key, CultureInfo.InvariantCulture));
            return new UdoData(gs, gs.GetByParams(p));
        }

        /// <summary>Ajoute l'objet ; renvoie le DocEntry (documents) ou 0.</summary>
        public int Add()
        {
            GeneralDataParams p = _service.Add(Data);
            try
            {
                return Convert.ToInt32(p.GetProperty("DocEntry"), CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0; // donnée de base : pas de DocEntry
            }
        }

        public void Update()
        {
            _service.Update(Data);
        }

        /// <summary>Supprime un document (DocEntry) ou une donnée de base (Code) ; l'objet doit autoriser la suppression.</summary>
        public static void Delete(string objectCode, object key)
        {
            GeneralService gs = Service(objectCode);
            GeneralDataParams p = (GeneralDataParams)gs.GetDataInterface(GeneralServiceDataInterfaces.gsGeneralDataParams);
            if (key is int i)
                p.SetProperty("DocEntry", i);
            else
                p.SetProperty("Code", Convert.ToString(key, CultureInfo.InvariantCulture));
            gs.Delete(p);
        }

        public void Set(string field, object value)
        {
            Data.SetProperty(field, value ?? "");
        }

        /// <summary>
        /// Vide un champ date : le GeneralService refuse DBNull ; selon les
        /// versions il accepte une chaîne vide ou la date « zéro » de SAP.
        /// </summary>
        public void ClearDate(string field)
        {
            try
            {
                Data.SetProperty(field, "");
            }
            catch (COMException)
            {
                Data.SetProperty(field, new DateTime(1899, 12, 30));
            }
        }

        public string Str(string field)
        {
            object v = Data.GetProperty(field);
            return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture).Trim();
        }

        public double Dbl(string field)
        {
            object v = Data.GetProperty(field);
            if (v == null)
                return 0;
            return v is string s ? Sql.ParseDouble(s) : Convert.ToDouble(v, CultureInfo.InvariantCulture);
        }

        public DateTime? Date(string field)
        {
            object v = Data.GetProperty(field);
            if (v is DateTime d)
                return d.Year < 1901 ? (DateTime?)null : d.Date;
            return null;
        }

        public GeneralDataCollection Lines(string childTable)
        {
            return Data.Child(childTable);
        }

        public static IEnumerable<GeneralData> Each(GeneralDataCollection lines)
        {
            for (int i = 0; i < lines.Count; i++)
                yield return lines.Item(i);
        }

        public static string LineStr(GeneralData line, string field)
        {
            object v = line.GetProperty(field);
            return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture).Trim();
        }

        public static double LineDbl(GeneralData line, string field)
        {
            object v = line.GetProperty(field);
            if (v == null)
                return 0;
            return v is string s ? Sql.ParseDouble(s) : Convert.ToDouble(v, CultureInfo.InvariantCulture);
        }

        public static int LineId(GeneralData line)
        {
            return Convert.ToInt32(line.GetProperty("LineId"), CultureInfo.InvariantCulture);
        }

        /// <summary>Ligne enfant par LineId (ou null).</summary>
        public static GeneralData FindLine(GeneralDataCollection lines, int lineId)
        {
            foreach (GeneralData l in Each(lines))
                if (LineId(l) == lineId)
                    return l;
            return null;
        }
    }
}
