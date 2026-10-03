using System;
using System.Globalization;
using SAPbouiCOM;
using MaintenanceAddon.Core;
using MaintenanceAddon.Services;

namespace MaintenanceAddon.Forms
{
    /// <summary>
    /// Saisie des documents de mesure et relevés de compteur (IK11 / IK17).
    /// Une mesure hors limites propose la création d'un avis.
    /// </summary>
    internal sealed class MeasurementForm : SimpleForm
    {
        private const string DtHist = "dtMeas";

        public MeasurementForm(Application app) : base(app) { }

        protected override string FormType => FormIds.MeasureForm;
        protected override string Title => "Relevés de compteurs et mesures";
        protected override int FormWidth => 760;
        protected override int FormHeight => 520;

        public void Show(string equip, string point)
        {
            Open();
            if (!string.IsNullOrEmpty(equip))
            {
                SetUds("udEq", equip);
                LoadEquipment(point);
            }
        }

        protected override void Build()
        {
            const int lw = 120, x = 10;
            int y = 10;
            AddUds("udEq", BoDataType.dt_SHORT_TEXT, 50);
            AddUds("udEqNm", BoDataType.dt_SHORT_TEXT, 100);
            AddUds("udPt", BoDataType.dt_SHORT_TEXT, 20);
            AddUds("udLast", BoDataType.dt_SHORT_TEXT, 200);
            AddUds("udDate", BoDataType.dt_DATE);
            AddUds("udTime", BoDataType.dt_SHORT_TEXT, 5);
            AddUds("udVal", BoDataType.dt_MEASURE);
            AddUds("udRem", BoDataType.dt_SHORT_TEXT, 200);

            U.Cfl("cflEq", Obj.Equip);
            U.Label("lEq", "Équipement", x, y, lw, "eEq");
            Ui.BindCfl(U.EditUds("eEq", x + lw, y, 120, "udEq"), "cflEq", "Code");
            U.ReadOnlyUds("eEqNm", x + lw + 125, y, 300, "udEqNm");
            y += Ui.Step;
            U.Label("lPt", "Point de mesure", x, y, lw, "cPt");
            U.ComboUds("cPt", x + lw, y, 425, "udPt");
            y += Ui.Step;
            U.ReadOnlyUds("eLast", x + lw, y, 425, "udLast");
            y += Ui.Step + 8;

            U.Label("lDate", "Date / heure", x, y, lw, "eDate");
            U.EditUds("eDate", x + lw, y, 100, "udDate");
            U.EditUds("eTime", x + lw + 105, y, 50, "udTime");
            y += Ui.Step;
            U.Label("lVal", "Valeur relevée", x, y, lw, "eVal");
            U.EditUds("eVal", x + lw, y, 100, "udVal");
            y += Ui.Step;
            U.Label("lRem", "Commentaire", x, y, lw, "eRem");
            U.EditUds("eRem", x + lw, y, 425, "udRem");
            y += Ui.Step + 4;
            U.Button("bSave", "Enregistrer le relevé", x + lw, y, 150);
            y += 30;

            U.Label("lHist", "Derniers relevés du point", x, y, 300);
            y += Ui.Step;
            U.Grid("gHist", DtHist, x, y, FormWidth - 30, FormHeight - y - 75);
            U.Button("bClose", "Fermer", x, FormHeight - 62, 90);
            ResetEntry();
        }

        private void ResetEntry()
        {
            SetUds("udDate", DateValue(DateTime.Today));
            SetUds("udTime", DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture));
            SetUds("udVal", "");
            SetUds("udRem", "");
        }

        private void LoadEquipment(string point)
        {
            string equip = Uds("udEq").Trim();
            SetUds("udEqNm", NotificationService.NameOf(Db.Equip, equip));
            ComboBox c = (ComboBox)F.Items.Item("cPt").Specific;
            SetUds("udPt", "");
            Ui.Fill(c, null, false);
            string first = null;
            foreach (MeasuringPoint p in MeasurementService.Points(equip))
            {
                c.ValidValues.Add(p.Point, p.Point + " - " + p.Descr + (p.IsCounter ? " (compteur, " : " (") + p.Unit + ")");
                if (first == null || p.Point == point)
                    first = p.Point;
            }
            if (first == null)
                Msg("Aucun point de mesure sur cet équipement (onglet « Points de mesure » de la fiche).", BoStatusBarMessageType.smt_Warning);
            SetUds("udPt", first ?? "");
            ShowPoint();
        }

        private void ShowPoint()
        {
            string equip = Uds("udEq").Trim(), point = Uds("udPt").Trim();
            Row last = point == "" ? null : MeasurementService.LastReading(equip, point);
            MeasuringPoint mp = point == "" ? null : MeasurementService.Point(equip, point);
            string limits = mp != null && (mp.Min != 0 || mp.Max != 0) ? " - limites " + mp.Min + " / " + mp.Max + " " + mp.Unit : "";
            SetUds("udLast", last == null
                ? (point == "" ? "" : "Aucun relevé" + limits)
                : "Dernier relevé : " + last.Dbl("U_Value").ToString("N2", CultureInfo.GetCultureInfo("fr-FR")) + " " + (mp?.Unit ?? "") +
                  " le " + (last.Date("U_MDate")?.ToString("dd/MM/yyyy") ?? "") + limits);

            var f = new ReportFilter { From = new DateTime(1900, 1, 1), To = new DateTime(2999, 12, 31), Equip = equip == "" ? "\u0001" : equip };
            string sql = ReportService.View("MEA").Sql(f);
            if (point != "")
                sql = sql.Replace(" ORDER BY", " AND m.\"U_Point\" = " + Sql.Q(point) + " ORDER BY");
            U.LoadGrid("gHist", DtHist, sql);
        }

        protected override void OnEvent(ItemEvent e)
        {
            switch (e.EventType)
            {
                case BoEventTypes.et_CHOOSE_FROM_LIST:
                    if (e.ItemUID == "eEq")
                    {
                        string code = Chosen(e, "Code");
                        if (code != null)
                        {
                            SetUds("udEq", code);
                            LoadEquipment(null);
                        }
                    }
                    break;
                case BoEventTypes.et_VALIDATE:
                    if (e.ItemUID == "eEq" && e.ItemChanged)
                        LoadEquipment(null);
                    break;
                case BoEventTypes.et_COMBO_SELECT:
                    if (e.ItemUID == "cPt")
                        ShowPoint();
                    break;
                case BoEventTypes.et_ITEM_PRESSED:
                    if (!e.ActionSuccess)
                        break;
                    if (e.ItemUID == "bClose")
                        Close();
                    else if (e.ItemUID == "bSave")
                        Save();
                    break;
            }
        }

        private void Save()
        {
            string equip = Uds("udEq").Trim(), point = Uds("udPt").Trim();
            if (equip == "" || point == "")
            {
                Msg("Choisissez l'équipement et le point de mesure.", BoStatusBarMessageType.smt_Warning);
                return;
            }
            DateTime? date = Sql.ParseDate(Uds("udDate"));
            if (date == null)
            {
                Msg("Saisissez la date du relevé.", BoStatusBarMessageType.smt_Warning);
                return;
            }
            string valueText = Uds("udVal");
            if (string.IsNullOrWhiteSpace(valueText))
            {
                Msg("Saisissez la valeur relevée.", BoStatusBarMessageType.smt_Warning);
                return;
            }
            int.TryParse(Uds("udTime").Replace(":", "").Trim(), out int hhmm);

            MeasurementResult res = MeasurementService.Record(equip, point, date.Value, hhmm, Sql.ParseDouble(valueText), Uds("udRem"));
            Msg("Relevé enregistré" + (res.Difference != 0 ? " (écart " + res.Difference.ToString("N2", CultureInfo.GetCultureInfo("fr-FR")) + ")" : "") + ".");
            ResetEntry();
            ShowPoint();

            if (res.OutOfRange != null && Confirm("Mesure hors limites :\n" + res.OutOfRange + "\n\nCréer un avis de maintenance ?"))
            {
                int notif = NotificationService.CreateFromMeasurement(equip, point, "Mesure hors limites - " + res.OutOfRange);
                MeasurementService.LinkNotification(equip, point, notif);
                Navigator.Open(Obj.Notif, notif.ToString(CultureInfo.InvariantCulture));
            }
        }
    }
}
