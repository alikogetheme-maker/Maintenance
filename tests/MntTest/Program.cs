using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using SAPbobsCOM;
using MaintenanceAddon;
using MaintenanceAddon.Core;
using MaintenanceAddon.Models;
using MaintenanceAddon.Services;
using MaintenanceAddon.Setup;

namespace MntTest
{
    internal static class Program
    {
        private static int _ok, _ko;
        private static readonly string S = DateTime.Now.ToString("HHmmss");
        private const string Item = "CAS_HNKB_50";
        private const string Whs = "ABJ";

        private static void Check(bool cond, string label)
        {
            if (cond) { _ok++; Console.WriteLine("  OK  " + label); }
            else { _ko++; Console.WriteLine("  KO  " + label); }
        }

        private static void Fails(Action a, string label, string expectedFragment = null)
        {
            try { a(); _ko++; Console.WriteLine("  KO  (pas d'erreur) " + label); }
            catch (Exception ex)
            {
                bool match = expectedFragment == null || ex.Message.Contains(expectedFragment);
                if (match) { _ok++; Console.WriteLine("  OK  " + label + " -> " + ex.Message.Split('\n')[0]); }
                else { _ko++; Console.WriteLine("  KO  " + label + " -> message inattendu : " + ex.Message); }
            }
        }

        private static void Step(string title, Action a)
        {
            Console.WriteLine("== " + title);
            try { a(); }
            catch (Exception ex) { _ko++; Console.WriteLine("  KO  EXCEPTION : " + ex); }
        }

        [STAThread]
        private static int Main()
        {
            var cfg = XDocument.Load(@"C:\Users\administrator\source\repos\SAPMover\SAPMover\App.config")
                .Descendants("add").ToDictionary(e => (string)e.Attribute("key"), e => (string)e.Attribute("value"));

            var c = new Company
            {
                Server = "SRVBNSERP",
                DbServerType = BoDataServerTypes.dst_MSSQL2022,
                CompanyDB = "TST_TST",
                UserName = cfg["UserA"],
                Password = cfg["PassA"],
                LicenseServer = "SRVBNSERP.bns.local:40000",
                SLDServer = "SRVBNSERP.bns.local:40000",
                language = BoSuppLangs.ln_French
            };
            if (c.Connect() != 0)
            {
                c.GetLastError(out int code, out string msg);
                Console.WriteLine("Connexion DI impossible : " + code + " " + msg);
                return 2;
            }
            Console.WriteLine("Connecté à " + c.CompanyDB + " (" + c.UserName + "), suffixe de test " + S);
            DiCompany.Use(c);

            Step("Schéma", () =>
            {
                MetadataSetup.Progress = m => Console.WriteLine("     " + m);
                bool created = MetadataSetup.EnsureSchema();
                Console.WriteLine("     créations : " + created);
                Check(!MetadataSetup.EnsureSchema(), "2e passage : rien à créer (idempotent)");
                foreach (string o in new[] { Obj.FuncLoc, Obj.Equip, Obj.WorkCtr, Obj.Catalog, Obj.EqCat, Obj.TaskList, Obj.Plan, Obj.Notif, Obj.Order })
                    Check(Sql.Exists("SELECT 1 FROM \"OUDO\" WHERE \"Code\" = " + Sql.Q(o)), "UDO " + o);
                Check(Sql.Exists("SELECT 1 FROM \"CUFD\" WHERE \"TableID\" = 'IGE1' AND \"AliasID\" = 'MNT_Ord'"), "Champ IGE1.U_MNT_Ord");
                Check(Sql.Exists("SELECT 1 FROM \"CUFD\" WHERE \"TableID\" = 'PCH1' AND \"AliasID\" = 'MNT_Line'"), "Champ PCH1.U_MNT_Line");
                Check(NotificationService.CatalogCodes(CatalogTypes.Damage).Count >= 6, "Catalogues de départ");
            });

            Step("Paramètres", () =>
            {
                var s = SettingsService.Load();
                s.ExpenseAccount = "62430000";
                s.LaborExpenseAccount = "62480000";
                s.LaborAbsorptionAccount = "62410000";
                s.PostLabor = true;
                s.Dimension = 2;
                s.DefaultWarehouse = Whs;
                s.CapitalAccount = "23900000";
                SettingsService.Save(s);
                var r = SettingsService.Load();
                Check(r.ExpenseAccount == "62430000" && r.PostLabor && r.Dimension == 2, "Paramètres enregistrés et relus");
                Fails(() => { var b = SettingsService.Load(); b.ExpenseAccount = "ZZZ"; SettingsService.Save(b); }, "Compte inconnu refusé", "inconnu");
                var wc = UdoData.Get(Obj.WorkCtr, "MECA");
                wc.Set("U_Rate", 5000.0);
                wc.Update();
                Check(Sql.ScalarDbl("SELECT \"U_Rate\" FROM \"@MNT_OWCT\" WHERE \"Code\" = 'MECA'") == 5000, "Taux MECA = 5000");
            });

            string fl = "TS" + S, fl2 = "TL" + S, eq = "TEQ" + S, tsk = "TG" + S;
            Step("Données de base", () =>
            {
                var f = UdoData.New(Obj.FuncLoc);
                f.Set("Code", fl); f.Set("Name", "Site de test " + S); f.Set("U_Active", "Y"); f.Set("U_OcrCode", "0101CH01"); f.Set("U_Whs", Whs);
                f.Add();
                var f2 = UdoData.New(Obj.FuncLoc);
                f2.Set("Code", fl2); f2.Set("Name", "Ligne de test"); f2.Set("U_Parent", fl); f2.Set("U_Active", "Y");
                f2.Add();

                var e = UdoData.New(Obj.Equip);
                e.Set("Code", eq); e.Set("Name", "Chariot de test"); e.Set("U_FuncLoc", fl2); e.Set("U_Status", "A");
                e.Set("U_Category", "VEHI"); e.Set("U_WorkCtr", "MECA"); e.Set("U_Critic", "A");
                e.Set("U_StartUp", DateTime.Today.AddYears(-2)); e.Set("U_AcqValue", 1500000.0);
                var pts = e.Lines(Db.EquipPts);
                var p1 = pts.Add(); p1.SetProperty("U_Point", "H"); p1.SetProperty("U_Descr", "Heures moteur"); p1.SetProperty("U_Unit", "h"); p1.SetProperty("U_Counter", "Y"); p1.SetProperty("U_AnnEst", 2000.0);
                var p2 = pts.Add(); p2.SetProperty("U_Point", "TEMP"); p2.SetProperty("U_Descr", "Température huile"); p2.SetProperty("U_Unit", "°C"); p2.SetProperty("U_Counter", "N"); p2.SetProperty("U_MinVal", 10.0); p2.SetProperty("U_MaxVal", 90.0);
                e.Add();

                var info = EquipmentInfo.Load(eq);
                Check(info != null && info.FuncLoc == fl2 && info.WorkCtr == "MECA", "Équipement créé");
                Check(info.OcrCode == "0101CH01" && info.Whs == Whs, "Centre de coûts et magasin hérités du site (poste technique supérieur)");
                Check(MeasurementService.Points(eq).Count == 2, "2 points de mesure");

                var t = UdoData.New(Obj.TaskList);
                t.Set("Code", tsk); t.Set("Name", "Révision 500 h"); t.Set("U_OrdType", "PM02"); t.Set("U_Active", "Y"); t.Set("U_WorkCtr", "MECA");
                var ops = t.Lines(Db.TaskOps);
                var o1 = ops.Add(); o1.SetProperty("U_OpNo", "0010"); o1.SetProperty("U_Descr", "Vidange moteur"); o1.SetProperty("U_WorkCtr", "MECA"); o1.SetProperty("U_CtrlKey", "INT"); o1.SetProperty("U_PlanHrs", 2.0); o1.SetProperty("U_NbPers", 1);
                var o2 = ops.Add(); o2.SetProperty("U_OpNo", "0020"); o2.SetProperty("U_Descr", "Contrôle freins (sous-traité)"); o2.SetProperty("U_CtrlKey", "EXT"); o2.SetProperty("U_ExtCost", 25000.0); o2.SetProperty("U_Vendor", "F00000001");
                var cp = t.Lines(Db.TaskComps).Add(); cp.SetProperty("U_ItemCode", Item); cp.SetProperty("U_ItemName", "Pièce test"); cp.SetProperty("U_Qty", 3.0); cp.SetProperty("U_Whs", Whs); cp.SetProperty("U_OpNo", "0010"); cp.SetProperty("U_Proc", "S");
                t.Add();
                Check(Sql.ScalarDbl("SELECT COUNT(*) FROM \"@MNT_TSK1\" WHERE \"Code\" = " + Sql.Q(tsk)) == 2, "Gamme : 2 opérations");
            });

            int notif = 0, order = 0;
            Step("Avis de panne → ordre", () =>
            {
                var n = UdoData.New(Obj.Notif);
                n.Set("U_Type", "M2"); n.Set("U_Status", "OSNO"); n.Set("U_Equip", eq); n.Set("U_FuncLoc", fl2); n.Set("U_Priority", "2");
                n.Set("U_Subject", "Fuite d'huile test " + S); n.Set("U_Breakdwn", "Y");
                n.Set("U_MalfStD", DateTime.Today.AddDays(-1)); n.Set("U_RepDate", DateTime.Today); n.Set("U_ReqStart", DateTime.Today); n.Set("U_ReqEnd", DateTime.Today.AddDays(1));
                n.Set("U_Damage", "D-FUI"); n.Set("U_Cause", "C-USN");
                notif = n.Add();
                Check(notif > 0, "Avis créé (DocEntry " + notif + ")");
                Check(Sql.ScalarDbl("SELECT \"DocNum\" FROM \"@MNT_ONOT\" WHERE \"DocEntry\" = " + notif) > 0, "Avis numéroté (DocNum)");

                order = NotificationService.CreateOrder(notif);
                Check(order > 0, "Ordre créé depuis l'avis (DocEntry " + order + ")");
                Check(Sql.ScalarStr("SELECT \"U_Status\" FROM \"@MNT_ONOT\" WHERE \"DocEntry\" = " + notif) == "NOPR", "Avis en cours (NOPR)");
                Check(OrderService.Status(order) == "CRTD", "Ordre créé (CRTD)");
                Check(Sql.ScalarStr("SELECT \"U_OrdType\" FROM \"@MNT_OORD\" WHERE \"DocEntry\" = " + order) == "PM01", "Type PM01");
                Fails(() => NotificationService.CreateOrder(notif), "2e ordre sur le même avis refusé", "déjà rattaché");
                Fails(() => NotificationService.Complete(notif, DateTime.Today), "Avis non terminable tant que l'ordre est ouvert", "pas clôturé techniquement");
                Fails(() => OrderService.IssueComponents(order, new[] { new Movement { LineId = 1, Qty = 1 } }, DateTime.Today), "Sortie refusée sur ordre non lancé", "lancé");
            });

            Step("Ordre : composants et opérations ajoutés (comme l'écran)", () =>
            {
                var o = UdoData.Get(Obj.Order, order);
                var op = o.Lines(Db.OrderOps).Item(0);
                op.SetProperty("U_PlanHrs", 3.0);
                var op2 = o.Lines(Db.OrderOps).Add();
                op2.SetProperty("U_OpNo", "0020"); op2.SetProperty("U_Descr", "Réparation externe"); op2.SetProperty("U_CtrlKey", "EXT"); op2.SetProperty("U_ExtCost", 40000.0); op2.SetProperty("U_Vendor", "F00000001"); op2.SetProperty("U_Done", "N");
                var c1 = o.Lines(Db.OrderComps).Add();
                c1.SetProperty("U_ItemCode", Item); c1.SetProperty("U_ItemName", "Pièce test"); c1.SetProperty("U_Qty", 4.0); c1.SetProperty("U_Whs", Whs); c1.SetProperty("U_Proc", "S");
                var c2 = o.Lines(Db.OrderComps).Add();
                c2.SetProperty("U_ItemCode", "CONSIG_12"); c2.SetProperty("U_ItemName", "Article non stocké"); c2.SetProperty("U_Qty", 2.0); c2.SetProperty("U_Proc", "P");
                o.Update();
                OrderService.RecalcCosts(order);
                Row h = Sql.First("SELECT * FROM \"@MNT_OORD\" WHERE \"DocEntry\" = " + order);
                Check(Math.Abs(h.Dbl("U_PlLab") - 15000) < 0.01, "Prévu MO = 3 h × 5000 = 15000 (" + h.Dbl("U_PlLab") + ")");
                Check(Math.Abs(h.Dbl("U_PlExt") - 40000) < 0.01, "Prévu prestations = 40000 (" + h.Dbl("U_PlExt") + ")");
                double unit = OrderService.ItemCost(Item, Whs);
                double unitP = OrderService.ItemCost("CONSIG_12", "");
                Check(unit > 0 && Math.Abs(h.Dbl("U_PlMat") - (4 * unit + 2 * unitP)) < 0.01,
                      "Prévu pièces = 4 × " + unit + " (stock, coût magasin FIFO) + 2 × " + unitP + " (achat) = " + h.Dbl("U_PlMat"));
                Check(h.Str("U_OcrCode") == "0101CH01", "Centre de coûts hérité sur l'ordre");
            });

            Step("Lancement, sorties / retours de stock", () =>
            {
                OrderService.Release(order);
                Check(OrderService.Status(order) == "REL", "Ordre lancé (REL)");
                Fails(() => OrderService.Release(order), "Double lancement refusé", "statut");
                double stockBefore = Sql.ScalarDbl("SELECT \"OnHand\" FROM \"OITW\" WHERE \"ItemCode\" = " + Sql.Q(Item) + " AND \"WhsCode\" = " + Sql.Q(Whs));
                int compLine = (int)Sql.ScalarDbl("SELECT \"LineId\" FROM \"@MNT_ORD2\" WHERE \"DocEntry\" = " + order + " AND \"U_ItemCode\" = " + Sql.Q(Item));
                int nsLine = (int)Sql.ScalarDbl("SELECT \"LineId\" FROM \"@MNT_ORD2\" WHERE \"DocEntry\" = " + order + " AND \"U_ItemCode\" = 'CONSIG_12'");

                string gi = OrderService.IssueComponents(order, new[] { new Movement { LineId = compLine, Qty = 4 } }, DateTime.Today);
                Check(gi != "", "Sortie de stock n° " + gi);
                Check(Math.Abs(OrderService.IssuedQty(order, compLine) - 4) < 0.001, "Quantité sortie = 4");
                Check(Sql.ScalarDbl("SELECT \"U_IssQty\" FROM \"@MNT_ORD2\" WHERE \"DocEntry\" = " + order + " AND \"LineId\" = " + compLine) == 4, "U_IssQty mis à jour sur l'ordre");
                Row gl = Sql.First("SELECT TOP 1 l.\"AcctCode\", l.\"OcrCode2\", l.\"StockSum\" FROM \"IGE1\" l WHERE l.\"U_MNT_Ord\" = " + order);
                Check(gl != null && gl.Str("AcctCode") == "62430000", "Sortie imputée sur 62430000 (" + gl?.Str("AcctCode") + ")");
                Check(gl.Str("OcrCode2") == "0101CH01", "Sortie imputée sur le centre de coûts axe 2 (" + gl.Str("OcrCode2") + "), valeur " + gl.Dbl("StockSum"));
                double acMat = Sql.ScalarDbl("SELECT \"U_AcMat\" FROM \"@MNT_OORD\" WHERE \"DocEntry\" = " + order);
                Check(Math.Abs(acMat - gl.Dbl("StockSum")) < 0.01, "Réel pièces = valeur de la sortie (" + acMat + ")");
                Check(Math.Abs(Sql.ScalarDbl("SELECT \"OnHand\" FROM \"OITW\" WHERE \"ItemCode\" = " + Sql.Q(Item) + " AND \"WhsCode\" = " + Sql.Q(Whs)) - (stockBefore - 4)) < 0.001, "Stock diminué de 4");

                Fails(() => OrderService.ReturnComponents(order, new[] { new Movement { LineId = compLine, Qty = 5 } }, DateTime.Today), "Retour > sorti refusé", "supérieur");
                Fails(() => OrderService.IssueComponents(order, new[] { new Movement { LineId = nsLine, Qty = 1 } }, DateTime.Today), "Sortie d'un article non stocké refusée", "pas géré en stock");
                string gr = OrderService.ReturnComponents(order, new[] { new Movement { LineId = compLine, Qty = 4 } }, DateTime.Today);
                Check(gr != "", "Retour en stock n° " + gr);
                Check(Math.Abs(OrderService.IssuedQty(order, compLine)) < 0.001, "Quantité nette sortie = 0");
                Check(Math.Abs(Sql.ScalarDbl("SELECT \"U_AcMat\" FROM \"@MNT_OORD\" WHERE \"DocEntry\" = " + order)) < 0.01, "Réel pièces revenu à 0 (retour au coût de sortie)");
                Check(Math.Abs(Sql.ScalarDbl("SELECT \"OnHand\" FROM \"OITW\" WHERE \"ItemCode\" = " + Sql.Q(Item) + " AND \"WhsCode\" = " + Sql.Q(Whs)) - stockBefore) < 0.001, "Stock revenu à l'initial");
            });

            Step("Confirmations de temps", () =>
            {
                int opLine = (int)Sql.ScalarDbl("SELECT \"LineId\" FROM \"@MNT_ORD1\" WHERE \"DocEntry\" = " + order + " AND \"U_OpNo\" = '0010'");
                OrderService.Confirm(order, new ConfirmationDraft { OpLineId = opLine, Date = DateTime.Today, Hours = 2.5, Remarks = "Test" });
                Row conf = Sql.First("SELECT TOP 1 * FROM \"@MNT_CONF\" WHERE \"U_OrderNo\" = " + order + " ORDER BY \"Code\" DESC");
                Check(conf != null && Math.Abs(conf.Dbl("U_Amount") - 12500) < 0.01, "Confirmation 2,5 h × 5000 = 12500");
                int je = conf.Int("U_TransId");
                Check(je > 0, "Écriture de main-d'oeuvre n° " + je);
                Row jl = Sql.First("SELECT SUM(\"Debit\") AS D, SUM(\"Credit\") AS C, MAX(CASE WHEN \"Account\" = '62480000' THEN \"OcrCode2\" END) AS O FROM \"JDT1\" WHERE \"TransId\" = " + je);
                Check(jl != null && Math.Abs(jl.Dbl("D") - 12500) < 0.01 && Math.Abs(jl.Dbl("C") - 12500) < 0.01, "Écriture équilibrée 12500");
                Check(jl.Str("O") == "0101CH01", "Charge de MO sur le centre de coûts de l'ordre");
                Check(Math.Abs(Sql.ScalarDbl("SELECT \"U_ActHrs\" FROM \"@MNT_ORD1\" WHERE \"DocEntry\" = " + order + " AND \"LineId\" = " + opLine) - 2.5) < 0.001, "Heures réalisées = 2,5");
                Check(Math.Abs(Sql.ScalarDbl("SELECT \"U_AcLab\" FROM \"@MNT_OORD\" WHERE \"DocEntry\" = " + order) - 12500) < 0.01, "Réel MO = 12500");
                Check(Sql.First("SELECT \"U_ActStart\" FROM \"@MNT_OORD\" WHERE \"DocEntry\" = " + order).Date("U_ActStart") == DateTime.Today, "Début réel renseigné");
                Fails(() => OrderService.Confirm(order, new ConfirmationDraft { OpLineId = opLine, Hours = -3 }), "Correction rendant le temps négatif refusée", "négatif");
                Fails(() => OrderService.Confirm(order, new ConfirmationDraft { OpLineId = opLine, Hours = 1, Date = DateTime.Today.AddDays(2) }), "Date future refusée", "futur");
                // Annulation par correction : les écritures se compensent
                OrderService.Confirm(order, new ConfirmationDraft { OpLineId = opLine, Date = DateTime.Today, Hours = -2.5, Remarks = "Correction test", Final = true });
                Check(Math.Abs(Sql.ScalarDbl("SELECT \"U_AcLab\" FROM \"@MNT_OORD\" WHERE \"DocEntry\" = " + order)) < 0.01, "Correction : réel MO revenu à 0");
                Check(Sql.ScalarStr("SELECT \"U_Done\" FROM \"@MNT_ORD1\" WHERE \"DocEntry\" = " + order + " AND \"LineId\" = " + opLine) == "Y", "Confirmation finale : opération terminée");
            });

            Step("Demandes d'achat et clôtures", () =>
            {
                string msg = OrderService.CreatePurchaseRequests(order);
                Console.WriteLine("     " + msg);
                Check(Sql.ScalarDbl("SELECT COUNT(DISTINCT l.\"DocEntry\") FROM \"PRQ1\" l WHERE l.\"U_MNT_Ord\" = " + order) == 2, "2 demandes d'achat (articles + prestations)");
                Check(Sql.ScalarDbl("SELECT COUNT(*) FROM \"@MNT_ORD2\" WHERE \"DocEntry\" = " + order + " AND \"U_Proc\" = 'P' AND \"U_PrEntry\" > 0") == 1, "Composant achat marqué (U_PrEntry)");
                Check(Sql.ScalarDbl("SELECT COUNT(*) FROM \"@MNT_ORD1\" WHERE \"DocEntry\" = " + order + " AND \"U_CtrlKey\" = 'EXT' AND \"U_PrEntry\" > 0") == 1, "Opération externe marquée (U_PrEntry)");
                Check(Sql.ScalarStr("SELECT TOP 1 \"AcctCode\" FROM \"PRQ1\" l JOIN \"OPRQ\" h ON h.\"DocEntry\" = l.\"DocEntry\" WHERE h.\"DocType\" = 'S' AND l.\"U_MNT_Ord\" = " + order) == "62430000", "Ligne de prestation sur le compte de charges");
                Fails(() => OrderService.CreatePurchaseRequests(order), "Pas de doublon de demande d'achat", "Aucune ligne");
                Fails(() => OrderService.Cancel(order), "Annulation refusée après confirmations", "confirmés");

                OrderService.TechnicallyComplete(order, DateTime.Today);
                Check(OrderService.Status(order) == "TECO", "Clôture technique (TECO)");
                Row n = Sql.First("SELECT * FROM \"@MNT_ONOT\" WHERE \"DocEntry\" = " + notif);
                Check(n.Str("U_Status") == "NOCO" && n.Date("U_MalfEnD") == DateTime.Today, "Avis terminé, fin de panne = date de TECO");

                OrderService.UndoTechnicalCompletion(order);
                Check(OrderService.Status(order) == "REL", "TECO annulée → REL");
                Row o = Sql.First("SELECT \"U_TecoDate\", \"U_ActEnd\" FROM \"@MNT_OORD\" WHERE \"DocEntry\" = " + order);
                Check(o.Date("U_TecoDate") == null && o.Date("U_ActEnd") == null, "Dates de TECO effacées");
                Check(Sql.ScalarStr("SELECT \"U_Status\" FROM \"@MNT_ONOT\" WHERE \"DocEntry\" = " + notif) == "NOPR", "Avis rouvert (NOPR)");
                OrderService.TechnicallyComplete(order, DateTime.Today);

                Fails(() => OrderService.Close(order), "Clôture refusée : demandes d'achat ouvertes", "ouverts");
                foreach (Row pr in Sql.Rows("SELECT DISTINCT l.\"DocEntry\" FROM \"PRQ1\" l WHERE l.\"U_MNT_Ord\" = " + order))
                {
                    var doc = (Documents)c.GetBusinessObject(BoObjectTypes.oPurchaseRequest);
                    doc.GetByKey(pr.Int("DocEntry"));
                    DiCompany.ThrowIfError(doc.Close(), "Clôture DA de test");
                }
                OrderService.Close(order);
                Check(OrderService.Status(order) == "CLSD", "Ordre clôturé (CLSD)");
            });

            Step("Annulation d'un ordre", () =>
            {
                var n = UdoData.New(Obj.Notif);
                n.Set("U_Type", "M1"); n.Set("U_Status", "OSNO"); n.Set("U_Equip", eq); n.Set("U_Priority", "3"); n.Set("U_Subject", "Demande test " + S); n.Set("U_Breakdwn", "N");
                int n2 = n.Add();
                int o2 = NotificationService.CreateOrder(n2);
                OrderService.Cancel(o2);
                Check(OrderService.Status(o2) == "CANC", "Ordre annulé");
                Row nr = Sql.First("SELECT * FROM \"@MNT_ONOT\" WHERE \"DocEntry\" = " + n2);
                Check(nr.Str("U_Status") == "OSNO" && nr.Int("U_OrderNo") == 0, "Avis remis en attente, sans ordre");
                int o3 = NotificationService.CreateOrder(n2);
                Check(o3 > 0, "Nouvel ordre possible après annulation");
                OrderService.Cancel(o3);
                var m3 = UdoData.New(Obj.Notif);
                m3.Set("U_Type", "M3"); m3.Set("U_Status", "OSNO"); m3.Set("U_Equip", eq); m3.Set("U_Priority", "4"); m3.Set("U_Subject", "Rapport test");
                int n3 = m3.Add();
                Fails(() => NotificationService.CreateOrder(n3), "M3 ne génère pas d'ordre", "M3");
                NotificationService.Complete(n3, DateTime.Today);
                Check(Sql.ScalarStr("SELECT \"U_Status\" FROM \"@MNT_ONOT\" WHERE \"DocEntry\" = " + n3) == "NOCO", "Avis M3 terminé");
                NotificationService.Reopen(n3);
                Row r3 = Sql.First("SELECT * FROM \"@MNT_ONOT\" WHERE \"DocEntry\" = " + n3);
                Check(r3.Str("U_Status") == "OSNO" && r3.Date("U_ComplDt") == null, "Avis rouvert, date de fin effacée");
            });

            Step("Relevés", () =>
            {
                MeasurementService.Record(eq, "H", DateTime.Today.AddDays(-2), 800, 100, "init");
                var r = MeasurementService.Record(eq, "H", DateTime.Today, 1430, 160, "");
                Check(Math.Abs(r.Difference - 60) < 0.001, "Écart compteur = 60");
                Check(Math.Abs(MeasurementService.CurrentCounter(eq, "H") - 160) < 0.001, "Compteur actuel = 160");
                Check(Sql.ScalarDbl("SELECT \"U_MTime\" FROM \"@MNT_MDOC\" WHERE \"U_Equip\" = " + Sql.Q(eq) + " AND \"U_Value\" = 160") == 1430, "Heure enregistrée (1430)");
                Fails(() => MeasurementService.Record(eq, "H", DateTime.Today, 1500, 150, ""), "Compteur en baisse refusé", "diminuer");
                Fails(() => MeasurementService.Record(eq, "H", DateTime.Today.AddDays(-1), 900, 170, ""), "Relevé antérieur refusé", "chronologique");
                var t = MeasurementService.Record(eq, "TEMP", DateTime.Today, 0, 95, "");
                Check(t.OutOfRange != null, "Mesure 95 °C hors limites : " + t.OutOfRange);
                int an = NotificationService.CreateFromMeasurement(eq, "TEMP", "Mesure hors limites - " + t.OutOfRange);
                MeasurementService.LinkNotification(eq, "TEMP", an);
                Check(Sql.ScalarDbl("SELECT COUNT(*) FROM \"@MNT_MDOC\" WHERE \"U_Equip\" = " + Sql.Q(eq) + " AND \"U_NotifNo\" = " + an) == 1, "Avis rattaché au relevé");
            });

            string pT = "TPT" + S, pC = "TPC" + S, pK = "TPK" + S;
            Step("Plans et ordonnancement", () =>
            {
                var p = UdoData.New(Obj.Plan);
                p.Set("Code", pT); p.Set("Name", "Révision mensuelle test"); p.Set("U_Type", "T"); p.Set("U_Equip", eq); p.Set("U_FuncLoc", fl2); p.Set("U_TaskList", tsk);
                p.Set("U_OrdType", "PM02"); p.Set("U_Priority", "3"); p.Set("U_Cycle", 1); p.Set("U_CycUnit", "M"); p.Set("U_LeadDays", 7); p.Set("U_Basis", "P");
                p.Set("U_StartDate", DateTime.Today.AddDays(-40)); p.Set("U_NextDate", DateTime.Today.AddDays(-40).AddMonths(1)); p.Set("U_Active", "Y");
                p.Add();

                var pc = UdoData.New(Obj.Plan);
                pc.Set("Code", pC); pc.Set("Name", "Vidange 100 h test"); pc.Set("U_Type", "C"); pc.Set("U_Equip", eq); pc.Set("U_TaskList", tsk);
                pc.Set("U_OrdType", "PM02"); pc.Set("U_Priority", "3"); pc.Set("U_Point", "H"); pc.Set("U_CycCount", 100.0); pc.Set("U_LeadCnt", 10.0);
                pc.Set("U_StartCnt", 50.0); pc.Set("U_NextCnt", 150.0); pc.Set("U_Basis", "P"); pc.Set("U_Active", "Y");
                pc.Add();

                var pk = UdoData.New(Obj.Plan);
                pk.Set("Code", pK); pk.Set("Name", "Contrôle depuis clôture test"); pk.Set("U_Type", "T"); pk.Set("U_Equip", eq); pk.Set("U_Cycle", 2); pk.Set("U_CycUnit", "W");
                pk.Set("U_LeadDays", 0); pk.Set("U_Basis", "C"); pk.Set("U_StartDate", DateTime.Today.AddDays(-20)); pk.Set("U_NextDate", DateTime.Today.AddDays(-6)); pk.Set("U_Active", "Y");
                pk.Add();

                var ov = PlanService.Overview(DateTime.Today);
                Check(ov.Any(d => d.PlanCode == pT && d.IsDue), "Plan temps dû");
                Check(ov.Any(d => d.PlanCode == pC && d.IsDue && Math.Abs(d.CurrentCounter - 160) < 0.001), "Plan compteur dû (160 ≥ 150 − 10)");
                var dC = ov.First(d => d.PlanCode == pC);
                Console.WriteLine("     date estimée compteur : " + dC.DueDate);

                DateTime next0 = Sql.First("SELECT \"U_NextDate\" FROM \"@MNT_OPLN\" WHERE \"Code\" = " + Sql.Q(pT)).Date("U_NextDate").Value;
                int oT = PlanService.Call(pT);
                Check(oT > 0 && Sql.ScalarDbl("SELECT COUNT(*) FROM \"@MNT_ORD1\" WHERE \"DocEntry\" = " + oT) == 2, "Ordre préventif avec les 2 opérations de la gamme");
                Check(Sql.ScalarDbl("SELECT COUNT(*) FROM \"@MNT_ORD2\" WHERE \"DocEntry\" = " + oT) == 1, "... et le composant de la gamme");
                Check(Sql.ScalarStr("SELECT \"U_PlanCode\" FROM \"@MNT_OORD\" WHERE \"DocEntry\" = " + oT) == pT, "Ordre rattaché au plan");
                Check(Sql.First("SELECT \"U_NextDate\" FROM \"@MNT_OPLN\" WHERE \"Code\" = " + Sql.Q(pT)).Date("U_NextDate") == next0.AddMonths(1), "Base « date planifiée » : échéance avancée d'un mois");
                Check(PlanService.OpenCallOrder(pT) == oT, "Appel ouvert enregistré");

                int oC = PlanService.Call(pC);
                Check(Math.Abs(Sql.ScalarDbl("SELECT \"U_NextCnt\" FROM \"@MNT_OPLN\" WHERE \"Code\" = " + Sql.Q(pC)) - 250) < 0.001, "Plan compteur : prochaine échéance 250");
                var ov2 = PlanService.Overview(DateTime.Today);
                Check(!ov2.First(d => d.PlanCode == pC).IsDue, "Plan compteur plus dû après l'appel");

                int oK = PlanService.Call(pK);
                Fails(() => PlanService.Call(pK), "Base « clôture » : pas de 2e appel tant que l'ordre est ouvert", "déjà un ordre");
                Check(PlanService.Overview(DateTime.Today).First(d => d.PlanCode == pK).State.Contains("en cours"), "État « Ordre en cours »");
                OrderService.Release(oK);
                OrderService.TechnicallyComplete(oK, DateTime.Today);
                Row pkr = Sql.First("SELECT * FROM \"@MNT_OPLN\" WHERE \"Code\" = " + Sql.Q(pK));
                Check(pkr.Date("U_NextDate") == DateTime.Today.AddDays(14) && pkr.Date("U_LastDate") == DateTime.Today, "Base « clôture » : échéance = TECO + 2 semaines");
                Check(Sql.ScalarStr("SELECT \"U_Status\" FROM \"@MNT_CALL\" WHERE \"U_OrderNo\" = " + oK) == "D", "Appel réalisé (D)");

                DateTime nextBefore = Sql.First("SELECT \"U_NextDate\" FROM \"@MNT_OPLN\" WHERE \"Code\" = " + Sql.Q(pT)).Date("U_NextDate").Value;
                PlanService.Skip(pT);
                Check(Sql.First("SELECT \"U_NextDate\" FROM \"@MNT_OPLN\" WHERE \"Code\" = " + Sql.Q(pT)).Date("U_NextDate") == nextBefore.AddMonths(1), "Échéance ignorée : +1 mois");
                OrderService.Cancel(oT);
                Check(Sql.ScalarStr("SELECT \"U_Status\" FROM \"@MNT_CALL\" WHERE \"U_OrderNo\" = " + oT) == "S", "Ordre de plan annulé : appel ignoré (S)");
                OrderService.Cancel(oC);
            });

            string flx = "TX" + S, clim = "TCL" + S, clim2 = "TCM" + S, clim3 = "TCN" + S, ctr = "TCT" + S;
            const string garant = "F00000001", presta = "F00000002", capAcct = "23900000";
            Step("Nettoyage des factures de test restées ouvertes", () =>
            {
                foreach (Row r in Sql.Rows("SELECT DISTINCT h.\"DocEntry\", h.\"CardCode\" FROM \"OPCH\" h JOIN \"PCH1\" l ON l.\"DocEntry\" = h.\"DocEntry\" " +
                                           "JOIN \"@MNT_OORD\" o ON o.\"DocEntry\" = l.\"U_MNT_Ord\" WHERE h.\"CANCELED\" = 'N' AND h.\"DocStatus\" = 'O' " +
                                           "AND o.\"U_Subject\" LIKE N'Remplacement unité extérieure%'"))
                {
                    var cmo = (Documents)c.GetBusinessObject(BoObjectTypes.oPurchaseCreditNotes);
                    cmo.CardCode = r.Str("CardCode"); cmo.DocType = BoDocumentTypes.dDocument_Service; cmo.DocDate = DateTime.Today;
                    double total = Sql.ScalarDbl("SELECT \"LineTotal\" FROM \"PCH1\" WHERE \"DocEntry\" = " + r.Int("DocEntry") + " AND \"LineNum\" = 0");
                    cmo.Lines.BaseType = 18; cmo.Lines.BaseEntry = r.Int("DocEntry"); cmo.Lines.BaseLine = 0; cmo.Lines.LineTotal = total;
                    DiCompany.ThrowIfError(cmo.Add(), "Avoir de nettoyage");
                    Check(true, "Avoir de nettoyage sur la facture " + r.Int("DocEntry"));
                }
            });
            Step("Lot 2 - garantie, prestataire attitré, contrat", () =>
            {
                var sp = SettingsService.Load();
                sp.CapitalAccount = "23900";
                Fails(() => SettingsService.Save(sp), "Compte collectif refusé en paramètre", "collectif");
                sp.CapitalAccount = capAcct;
                SettingsService.Save(sp);

                var f = UdoData.New(Obj.FuncLoc);
                f.Set("Code", flx); f.Set("Name", "Agence externe de test"); f.Set("U_Location", "Zone industrielle, Yopougon"); f.Set("U_Active", "Y");
                f.Set("U_OcrCode", "0101CH01"); f.Set("U_Whs", Whs);
                f.Add();
                Action<string, string, DateTime?, string> eqAdd = (code, name, warrEnd, mnt) =>
                {
                    var e = UdoData.New(Obj.Equip);
                    e.Set("Code", code); e.Set("Name", name); e.Set("U_FuncLoc", flx); e.Set("U_Status", "A"); e.Set("U_Category", "BATI");
                    e.Set("U_Vendor", garant); e.Set("U_MntVend", mnt); e.Set("U_WorkCtr", "EXT");
                    if (warrEnd.HasValue) e.Set("U_WarrEnd", warrEnd.Value);
                    e.Add();
                };
                eqAdd(clim, "Climatiseur bureau (garantie)", DateTime.Today.AddDays(100), presta);
                eqAdd(clim2, "Climatiseur salle serveur (contrat)", DateTime.Today.AddDays(-10), "");
                eqAdd(clim3, "Climatiseur entrepôt (prestataire attitré)", null, presta);

                var ci = EquipmentInfo.Load(clim);
                Check(ci.UnderWarranty(DateTime.Today) && ci.WarrantyVendor == garant && ci.MaintVendor == presta, "Équipement sous garantie, garant = fournisseur d'achat, prestataire attitré");
                Check(!EquipmentInfo.Load(clim2).UnderWarranty(DateTime.Today), "Garantie expirée détectée");

                var ct = UdoData.New(Obj.Contract);
                ct.Set("Code", ctr); ct.Set("Name", "Contrat climatisation test"); ct.Set("U_Vendor", presta); ct.Set("U_Type", "C");
                ct.Set("U_StartDt", DateTime.Today.AddMonths(-6)); ct.Set("U_EndDt", DateTime.Today.AddMonths(6)); ct.Set("U_Notice", 90);
                ct.Set("U_Amount", 1200000.0); ct.Set("U_Billing", "Q"); ct.Set("U_RespHrs", 4); ct.Set("U_Visits", 4);
                ct.Set("U_CovLab", "Y"); ct.Set("U_CovParts", "N"); ct.Set("U_Active", "Y");
                var l = ct.Lines(Db.ContractEq).Add(); l.SetProperty("U_Equip", clim2); l.SetProperty("U_EqName", "Climatiseur salle serveur");
                ct.Add();
                ContractInfo active = ContractService.ActiveFor(clim2, DateTime.Today);
                Check(active != null && active.Code == ctr && active.ResponseHours == 4 && active.CoversLabor, "Contrat actif trouvé pour l'équipement");
                Check(ContractService.ActiveFor(clim2, DateTime.Today.AddYears(1)) == null, "Pas de contrat après la date de fin");
                Check(active.Covers("PM01") && active.Covers("PM02") && !active.Covers("PM03"), "Contrat complet : correctif + préventif, pas les améliorations");
                Check(new ContractInfo { Type = "D" }.Covers("PM01") && !new ContractInfo { Type = "D" }.Covers("PM02"), "Contrat dépannage : correctif seulement");
                double exp = ContractService.ExpectedToDate(active, 1200000, DateTime.Today);
                Check(exp > 550000 && exp < 650000, "Prévu à date au prorata ≈ 6 mois (" + exp + ")");
                Check(Math.Abs(ContractService.Invoiced(ctr)) < 0.01, "Facturé sur le contrat = 0");
                Check(ServiceContext.For(clim2, DateTime.Today).Banner().Contains(ctr), "Bandeau : " + ServiceContext.For(clim2, DateTime.Today).Banner());
                Check(ServiceContext.For(clim, DateTime.Today).Banner().Contains("GARANTIE"), "Bandeau : " + ServiceContext.For(clim, DateTime.Today).Banner());
            });

            Step("Lot 2 - ordres sous garantie et sous contrat", () =>
            {
                Func<string, string, int> notifFor = (eqc, prio) =>
                {
                    var n = UdoData.New(Obj.Notif);
                    n.Set("U_Type", "M2"); n.Set("U_Status", "OSNO"); n.Set("U_Equip", eqc); n.Set("U_FuncLoc", flx); n.Set("U_Priority", prio);
                    n.Set("U_Subject", "Ne refroidit plus " + S); n.Set("U_Breakdwn", "N"); n.Set("U_RepDate", DateTime.Today); n.Set("U_ReqStart", DateTime.Today);
                    return n.Add();
                };

                int ow = NotificationService.CreateOrder(notifFor(clim, "3"));
                Row owh = Sql.First("SELECT * FROM \"@MNT_OORD\" WHERE \"DocEntry\" = " + ow);
                Row owo = Sql.First("SELECT * FROM \"@MNT_ORD1\" WHERE \"DocEntry\" = " + ow);
                Check(owh.Str("U_UnderWar") == "Y", "Ordre sous garantie");
                Check(owo.Str("U_CtrlKey") == "EXT" && owo.Str("U_Vendor") == garant && owo.Dbl("U_ExtCost") == 0 && owo.Str("U_Descr").StartsWith("Sous garantie"),
                      "Opération confiée au garant, coût 0 (" + owo.Str("U_Descr") + ")");
                Fails(() => OrderService.CreatePurchaseRequests(ow), "Pas de demande d'achat pour une intervention sous garantie", "garantie");

                int oc = NotificationService.CreateOrder(notifFor(clim2, "4"));
                Row och = Sql.First("SELECT * FROM \"@MNT_OORD\" WHERE \"DocEntry\" = " + oc);
                Row oco = Sql.First("SELECT * FROM \"@MNT_ORD1\" WHERE \"DocEntry\" = " + oc);
                Check(och.Str("U_Contract") == ctr && och.Str("U_UnderWar") == "N", "Ordre rattaché au contrat " + ctr);
                Check(och.Date("U_EndDt") <= DateTime.Today.AddDays(1),
                      "Fin prévue = délai d'intervention du contrat (4 h) au lieu de la priorité 4 (" + och.Date("U_EndDt")?.ToString("dd/MM") + ")");
                Check(oco.Str("U_Vendor") == presta && oco.Dbl("U_ExtCost") == 0, "Opération confiée au titulaire du contrat, coût 0");

                // Préventif sous contrat : la prestation de la gamme n'est pas payée en plus
                var p = UdoData.New(Obj.Plan);
                string pl = "TPL" + S;
                p.Set("Code", pl); p.Set("Name", "Entretien trimestriel clim"); p.Set("U_Type", "T"); p.Set("U_Equip", clim2); p.Set("U_TaskList", "TG" + S);
                p.Set("U_OrdType", "PM02"); p.Set("U_Cycle", 3); p.Set("U_CycUnit", "M"); p.Set("U_LeadDays", 7); p.Set("U_Basis", "P");
                p.Set("U_StartDate", DateTime.Today.AddMonths(-3)); p.Set("U_NextDate", DateTime.Today); p.Set("U_Active", "Y");
                p.Add();
                int op = PlanService.Call(pl);
                Check(Sql.ScalarStr("SELECT \"U_Contract\" FROM \"@MNT_OORD\" WHERE \"DocEntry\" = " + op) == ctr, "Ordre préventif rattaché au contrat");
                Row ext = Sql.First("SELECT * FROM \"@MNT_ORD1\" WHERE \"DocEntry\" = " + op + " AND \"U_CtrlKey\" = 'EXT'");
                Check(ext != null && ext.Dbl("U_ExtCost") == 0 && ext.Str("U_Vendor") == "F00000001", "Prestation de gamme couverte : coût 0 (prestataire de la gamme conservé)");
                Check(Math.Abs(Sql.ScalarDbl("SELECT \"U_PlExt\" FROM \"@MNT_OORD\" WHERE \"DocEntry\" = " + op)) < 0.01, "Prévu prestations = 0");
                foreach (int o in new[] { ow, oc, op })
                    OrderService.Cancel(o);
            });

            Step("Lot 2 - envoi chez le prestataire et retour", () =>
            {
                string code = ShipmentService.Send(clim, garant, 0, DateTime.Today, DateTime.Today.AddDays(10), "Compresseur à remplacer en atelier");
                Check(code != "", "Envoi enregistré");
                Check(EquipmentInfo.Load(clim).Status == EquipStatus.AtVendor, "Équipement « Chez le prestataire »");
                Check(ShipmentService.Describe(ShipmentService.OpenShipment(clim)).Contains("Ivoire Boissons"), ShipmentService.Describe(ShipmentService.OpenShipment(clim)));
                Fails(() => ShipmentService.Send(clim, garant, 0, DateTime.Today, null, ""), "Double envoi refusé", "déjà chez");
                Fails(() => ShipmentService.Return(clim, DateTime.Today.AddDays(-1), ""), "Retour avant l'envoi refusé", "précède");
                ShipmentService.Return(clim, DateTime.Today, "Réparé, compresseur neuf");
                Check(EquipmentInfo.Load(clim).Status == EquipStatus.Active, "Retour : équipement de nouveau « En service »");
                Check(ShipmentService.OpenShipment(clim) == null, "Plus d'envoi en cours");
                Fails(() => ShipmentService.Return(clim, DateTime.Today, ""), "Retour sans envoi refusé", "Aucun envoi");
                Fails(() => ShipmentService.Send(clim, "C_INCONNU", 0, DateTime.Today, null, ""), "Prestataire inconnu refusé", "fournisseur");
            });

            Step("Lot 2 - prestation achetée (DA → commande → facture) et immobilisation", () =>
            {
                var d = new OrderDraft { OrdType = "PM03", Equip = clim3, FuncLoc = flx, Subject = "Remplacement unité extérieure " + S, OcrCode = "0101CH01", Start = DateTime.Today };
                d.Ops.Add(new OpDraft { OpNo = "0010", Descr = "Fourniture et pose unité extérieure", CtrlKey = "EXT", ExtCost = 30000 });
                int o = OrderService.Create(d);
                Row h = Sql.First("SELECT * FROM \"@MNT_OORD\" WHERE \"DocEntry\" = " + o);
                Row opl = Sql.First("SELECT * FROM \"@MNT_ORD1\" WHERE \"DocEntry\" = " + o);
                Check(opl.Str("U_Vendor") == presta && opl.Dbl("U_ExtCost") == 30000, "Prestataire attitré repris, coût prévu conservé (pas de couverture)");
                Check(h.Str("U_Capital") == "Y" && h.Str("U_CapAcct") == capAcct, "PM03 : à immobiliser, compte " + h.Str("U_CapAcct"));

                OrderService.Release(o);
                OrderService.CreatePurchaseRequests(o);
                Row prl = Sql.First("SELECT l.\"DocEntry\", l.\"LineNum\", l.\"LineVendor\", l.\"LineTotal\", l.\"AcctCode\" FROM \"PRQ1\" l WHERE l.\"U_MNT_Ord\" = " + o);
                Check(prl != null && prl.Str("LineVendor") == presta && Math.Abs(prl.Dbl("LineTotal") - 30000) < 0.01, "DA de prestation : prestataire attitré, 30 000");

                // Commande puis facture par copie (comme « Copier vers » dans SAP)
                var po = (Documents)c.GetBusinessObject(BoObjectTypes.oPurchaseOrders);
                po.CardCode = presta; po.DocType = BoDocumentTypes.dDocument_Service; po.DocDate = DateTime.Today; po.DocDueDate = DateTime.Today;
                po.Lines.BaseType = 1470000113; po.Lines.BaseEntry = prl.Int("DocEntry"); po.Lines.BaseLine = prl.Int("LineNum");
                // En DI API, une ligne de service copiée ne reprend pas son montant (le client SAP, si)
                po.Lines.LineTotal = prl.Dbl("LineTotal");
                DiCompany.ThrowIfError(po.Add(), "Commande");
                int poEntry = int.Parse(c.GetNewObjectKey());
                double poOrd = Sql.ScalarDbl("SELECT \"U_MNT_Ord\" FROM \"POR1\" WHERE \"DocEntry\" = " + poEntry);
                Check(poOrd == o, "Commande par copie : n° d'ordre repris sur la ligne (" + poOrd + ")");
                Check(Sql.ScalarStr("SELECT \"LineStatus\" FROM \"PRQ1\" WHERE \"DocEntry\" = " + prl.Int("DocEntry")) == "C", "Demande d'achat soldée par la commande");
                Fails(() => { OrderService.TechnicallyComplete(o, DateTime.Today); OrderService.Close(o); }, "Clôture refusée tant que la commande est ouverte", "ouverts");

                var inv = (Documents)c.GetBusinessObject(BoObjectTypes.oPurchaseInvoices);
                inv.CardCode = presta; inv.DocType = BoDocumentTypes.dDocument_Service; inv.DocDate = DateTime.Today; inv.DocDueDate = DateTime.Today;
                inv.Lines.BaseType = 22; inv.Lines.BaseEntry = poEntry; inv.Lines.BaseLine = 0; inv.Lines.LineTotal = 30000;
                DiCompany.ThrowIfError(inv.Add(), "Facture fournisseur");
                int invEntry = int.Parse(c.GetNewObjectKey());
                Check(Sql.ScalarDbl("SELECT \"U_MNT_Ord\" FROM \"PCH1\" WHERE \"DocEntry\" = " + invEntry) == o, "Facture par copie : n° d'ordre repris");

                OrderService.RecalcCosts(o);
                double acExt = Sql.ScalarDbl("SELECT \"U_AcExt\" FROM \"@MNT_OORD\" WHERE \"DocEntry\" = " + o);
                Check(Math.Abs(acExt - 30000) < 0.01, "Réel prestations = facture fournisseur (" + acExt + ")");

                OrderService.Close(o);
                Row hc = Sql.First("SELECT * FROM \"@MNT_OORD\" WHERE \"DocEntry\" = " + o);
                int je = hc.Int("U_SettJE");
                Check(hc.Str("U_Status") == "CLSD" && je > 0 && Math.Abs(hc.Dbl("U_SettAmt") - 30000) < 0.01, "Clôture avec règlement : écriture " + je + ", " + hc.Dbl("U_SettAmt"));
                Row jd = Sql.First("SELECT SUM(CASE WHEN \"Account\" = '" + capAcct + "' THEN \"Debit\" - \"Credit\" ELSE 0 END) AS Cap, " +
                                   "SUM(CASE WHEN \"Account\" = '62430000' THEN \"Credit\" - \"Debit\" ELSE 0 END) AS Chg, " +
                                   "MAX(CASE WHEN \"Account\" = '62430000' THEN \"OcrCode2\" END) AS Ocr FROM \"JDT1\" WHERE \"TransId\" = " + je);
                Check(Math.Abs(jd.Dbl("Cap") - 30000) < 0.01 && Math.Abs(jd.Dbl("Chg") - 30000) < 0.01, "Règlement : débit " + capAcct + " / crédit 62430000 de 30 000");
                Check(jd.Str("Ocr") == "0101CH01", "Charge sortie du centre de coûts de l'ordre");

                // Nettoyage : avoir sur la facture et contre-passation du règlement
                var cm = (Documents)c.GetBusinessObject(BoObjectTypes.oPurchaseCreditNotes);
                cm.CardCode = presta; cm.DocType = BoDocumentTypes.dDocument_Service; cm.DocDate = DateTime.Today;
                cm.Lines.BaseType = 18; cm.Lines.BaseEntry = invEntry; cm.Lines.BaseLine = 0; cm.Lines.LineTotal = 30000;
                DiCompany.ThrowIfError(cm.Add(), "Avoir de test");
                var jr = (JournalEntries)c.GetBusinessObject(BoObjectTypes.oJournalEntries);
                jr.GetByKey(je);
                DiCompany.ThrowIfError(jr.Cancel(), "Contre-passation du règlement de test");
                Check(true, "Avoir et contre-passation de test enregistrés");
            });

            // ================= Lot 3 : intégration SAP =================
            const string serItem = "MNTTEST_SER";
            string sn = "SN" + S, eqSer = "TSE" + S;
            Step("Lot 3 - schéma : pièces de rechange, liens SAP, autorisations", () =>
            {
                Check(Sql.Exists("SELECT 1 FROM \"UDO1\" WHERE \"Code\" = 'MNT_EQUIP' AND \"TableName\" = 'MNT_EQP2'"), "Table des pièces de rechange rattachée à l'objet équipement");
                foreach (string f in new[] { "SerSys", "AssetNo", "AtcEntry" })
                    Check(Sql.Exists("SELECT 1 FROM \"CUFD\" WHERE \"TableID\" = '@MNT_OEQP' AND \"AliasID\" = " + Sql.Q(f)), "Champ équipement U_" + f);
                Check(Sql.ScalarDbl("SELECT COUNT(*) FROM \"OUPT\" WHERE \"AbsId\" LIKE 'MNT_AUTH%'") == 7, "7 autorisations Maintenance dans l'arbre SAP");
                Check(Sql.ScalarStr("SELECT \"FathId\" FROM \"OUPT\" WHERE \"AbsId\" = " + Sql.Q(Perm.Order)) == Perm.Root, "Autorisations rangées sous « Maintenance »");
                Check(AuthService.Level(Perm.Close) == Access.Full, "Super-utilisateur : autorisation complète");
                string plain = Sql.ScalarStr("SELECT TOP 1 \"USER_CODE\" FROM \"OUSR\" u WHERE \"SUPERUSER\" = 'N' AND ISNULL(\"Locked\", 'N') = 'N' " +
                                             "AND NOT EXISTS (SELECT 1 FROM \"USR3\" p WHERE p.\"UserLink\" = u.\"USERID\" AND p.\"PermId\" = " + Sql.Q(Perm.Order) + " AND p.\"Permission\" IN ('F', 'R'))");
                Check(plain != "" && AuthService.Level(Perm.Order, plain) == Access.None, "Utilisateur sans droit (" + plain + ") : aucune autorisation tant que l'administrateur ne l'a pas donnée");
                Check(!EquipmentService.FixedAssetsUsed(), "Module Immobilisations non utilisé dans TST_TST : champ immobilisation masqué");
            });

            Step("Lot 3 - n° de série reçu dans SAP → fiche équipement", () =>
            {
                if (!Sql.Exists("SELECT 1 FROM \"OITM\" WHERE \"ItemCode\" = " + Sql.Q(serItem)))
                {
                    var it = (Items)c.GetBusinessObject(BoObjectTypes.oItems);
                    it.ItemCode = serItem; it.ItemName = "Climatiseur split 2 CV (test maintenance)";
                    it.InventoryItem = BoYesNoEnum.tYES; it.PurchaseItem = BoYesNoEnum.tYES; it.SalesItem = BoYesNoEnum.tNO;
                    it.ManageSerialNumbers = BoYesNoEnum.tYES;
                    DiCompany.ThrowIfError(it.Add(), "Article de test géré par n° de série");
                }
                var pdn = (Documents)c.GetBusinessObject(BoObjectTypes.oPurchaseDeliveryNotes);
                pdn.CardCode = garant; pdn.DocDate = DateTime.Today; pdn.DocDueDate = DateTime.Today;
                pdn.Comments = "Test add-on Maintenance (n° de série)";
                pdn.Lines.ItemCode = serItem; pdn.Lines.Quantity = 1; pdn.Lines.WarehouseCode = Whs; pdn.Lines.UnitPrice = 450000;
                pdn.Lines.SerialNumbers.InternalSerialNumber = sn;
                pdn.Lines.SerialNumbers.ManufacturerSerialNumber = "MF" + S;
                pdn.Lines.SerialNumbers.WarrantyStart = DateTime.Today;
                pdn.Lines.SerialNumbers.WarrantyEnd = DateTime.Today.AddYears(2);
                pdn.Lines.SerialNumbers.Quantity = 1;
                DiCompany.ThrowIfError(pdn.Add(), "Réception de marchandises de test");
                string pdnNum = Sql.ScalarStr("SELECT \"DocNum\" FROM \"OPDN\" WHERE \"DocEntry\" = " + c.GetNewObjectKey());

                SerialInfo si = EquipmentService.Serial(serItem, sn);
                Check(si != null && si.Vendor == garant && si.MnfSerial == "MF" + S, "N° de série retrouvé avec son fournisseur et son n° fabricant");
                Check(si != null && si.DocLabel == "Réception de marchandises" && si.DocNum == pdnNum && si.DocDate == DateTime.Today, "Document d'entrée : " + si?.Source());
                Check(si != null && Math.Abs(si.UnitCost - 450000) < 0.01, "Valeur d'achat = prix de la réception (" + si?.UnitCost + ")");
                Check(si != null && si.WarrantyEnd == DateTime.Today.AddYears(2), "Fin de garantie reprise du n° de série");
                Check(Sql.Rows(EquipmentService.ReceivedSerialsSql(serItem, sn, true)).Count == 1, "Liste des n° reçus libres : 1 ligne");

                string code = EquipmentService.CreateFromSerial(serItem, sn, eqSer, null, fl2);
                Row e = Sql.First("SELECT * FROM " + Db.T(Db.Equip) + " WHERE \"Code\" = " + Sql.Q(code));
                Check(e != null && e.Str("U_ItemCode") == serItem && e.Str("U_SerialNo") == sn && e.Int("U_SerSys") == si.SysNumber, "Équipement créé depuis le n° de série (article, n°, n° système)");
                Check(e.Str("Name") == "Climatiseur split 2 CV (test maintenance)" && e.Str("U_Vendor") == garant && Math.Abs(e.Dbl("U_AcqValue") - 450000) < 0.01 &&
                      e.Date("U_AcqDate") == DateTime.Today && e.Date("U_WarrEnd") == DateTime.Today.AddYears(2),
                      "Désignation, fournisseur, date et valeur d'achat, garantie repris sans ressaisie");
                Check(e.Str("U_FuncLoc") == fl2, "Installé sur le poste technique indiqué");
                Check(EquipmentInfo.Load(code).UnderWarranty(DateTime.Today), "Garantie active pour les ordres");
                Fails(() => EquipmentService.CreateFromSerial(serItem, sn, "TSX" + S, null, null), "Même n° de série refusé pour un 2e équipement", "déjà rattaché");
                Check(Sql.Rows(EquipmentService.ReceivedSerialsSql(serItem, sn, true)).Count == 0, "N° rattaché : n'apparaît plus dans les n° libres");
                Check(Sql.Rows(EquipmentService.ReceivedSerialsSql(serItem, sn, false)).Any(r => r.Str("Equip") == code), "... mais visible (avec son équipement) sans le filtre");
                Check(EquipmentService.Duplicate(eq, serItem, sn, "", "") != null, "Doublon détecté à la saisie manuelle (même article, même n°)");
                Check(EquipmentService.Duplicate(code, serItem, sn, "", "") == null, "Pas de doublon avec lui-même");
                Check(EquipmentService.Duplicate(eq, "", "ABC" + S, "Daikin", "") == null, "N° libre sans article : accepté");
                Check(EquipmentService.CheckLinks(eq, Item, "", "", "ZZ_IMMO") != null, "Immobilisation inexistante refusée");
                Fails(() => EquipmentService.CreateFromSerial(serItem, "INCONNU" + S, null, null, null), "N° de série non reçu refusé", "n'a pas été reçu");
            });

            Step("Lot 3 - pièces de rechange et sécurité", () =>
            {
                var e = UdoData.Get(Obj.Equip, eq);
                var l = e.Lines(Db.EquipParts).Add();
                l.SetProperty("U_ItemCode", Item); l.SetProperty("U_ItemName", "Pièce test"); l.SetProperty("U_Qty", 2.0);
                var l2 = e.Lines(Db.EquipParts).Add();
                l2.SetProperty("U_ItemCode", "CONSIG_12"); l2.SetProperty("U_ItemName", "Article non stocké"); l2.SetProperty("U_Qty", 1.0);
                e.Update();
                List<Row> parts = EquipmentService.SpareParts(eq, Whs);
                Check(parts.Count == 2 && parts[0].Str("U_ItemCode") == Item && parts[0].Dbl("U_Qty") == 2, "2 pièces de rechange enregistrées sur l'équipement");
                double avail = Sql.ScalarDbl("SELECT \"OnHand\" - \"IsCommited\" FROM \"OITW\" WHERE \"ItemCode\" = " + Sql.Q(Item) + " AND \"WhsCode\" = " + Sql.Q(Whs));
                Check(Math.Abs(parts[0].Dbl("Dispo") - avail) < 0.001 && parts[1].Str("InvntItem") == "N", "Disponible du magasin (" + avail + ") et article non stocké signalé");
                Check(Sql.Rows(ReportService.View("SPR").Sql(new ReportFilter { Equip = eq })).Count == 2, "Rapport des pièces de rechange : 2 lignes");

                var t = UdoData.Get(Obj.TaskList, tsk);
                t.Set("U_Safety", "Consigner l'alimentation électrique. Gants et lunettes obligatoires.");
                t.Update();
                var d = new OrderDraft { OrdType = "PM02", Equip = eq, TaskList = tsk, Subject = "Bon de travail test " + S, Start = DateTime.Today };
                OrderService.LoadTaskList(tsk, d.Ops, d.Comps);
                int o = OrderService.Create(d);
                Check(Sql.ScalarStr("SELECT CAST(\"U_Safety\" AS NVARCHAR(4000)) FROM \"@MNT_OORD\" WHERE \"DocEntry\" = " + o).StartsWith("Consigner"), "Consignes de sécurité de la gamme reprises sur l'ordre");
                string html = WorkOrderPrint.Html(o);
                Check(html.Contains("BON DE TRAVAIL - Ordre n° " + OrderService.DocNum(o)) && html.Contains("CONSIGNES DE S"), "Bon de travail : titre et consignes de sécurité");
                Check(html.Contains(eq) && html.Contains("Vidange moteur") && html.Contains(Item) && html.Contains("Relevés à noter"), "Bon de travail : équipement, opérations, pièces, relevés");
                string path = WorkOrderPrint.Save(o);
                Check(System.IO.File.Exists(path) && path.EndsWith(".html"), "Bon de travail enregistré : " + path);
                OrderService.Cancel(o);
            });

            Step("Lot 3 - documents joints (pièces jointes SAP)", () =>
            {
                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MntTest" + S);
                System.IO.Directory.CreateDirectory(dir);
                string f1 = System.IO.Path.Combine(dir, "notice.txt"), f2 = System.IO.Path.Combine(dir, "certificat.txt");
                System.IO.File.WriteAllText(f1, "Notice de test");
                System.IO.File.WriteAllText(f2, "Certificat de test");
                int a1 = AttachmentService.AddToEquipment(eq, f1);
                int a2 = AttachmentService.AddToEquipment(eq, f2);
                Check(a1 > 0 && a1 == a2 && Sql.ScalarDbl("SELECT \"U_AtcEntry\" FROM " + Db.T(Db.Equip) + " WHERE \"Code\" = " + Sql.Q(eq)) == a1, "Pièce jointe SAP n° " + a1 + " rattachée à l'équipement");
                List<Row> files = AttachmentService.Files(a1);
                Check(files.Count == 2 && files[0].Str("FileName") == eq + "_notice", "2 documents, nommés d'après l'équipement (" + files[0].Str("FileName") + ")");
                string p1 = AttachmentService.FullPath(a1, files[0].Int("Line"));
                Check(System.IO.File.Exists(p1) && System.IO.File.ReadAllText(p1) == "Notice de test", "Fichier copié dans le dossier des pièces jointes de SAP");
                int a3 = AttachmentService.RemoveFromEquipment(eq, files[0].Int("Line"));
                List<Row> left = AttachmentService.Files(a3);
                Check(a3 > 0 && left.Count == 1 && left[0].Str("FileName") == eq + "_certificat", "Document retiré : il reste le certificat");
                Check(Sql.Rows(AttachmentService.FilesSql(a3)).Count == 1, "Grille des documents : 1 ligne");
                int a4 = AttachmentService.RemoveFromEquipment(eq, left[0].Int("Line"));
                Check(a4 == 0 && Sql.ScalarDbl("SELECT \"U_AtcEntry\" FROM " + Db.T(Db.Equip) + " WHERE \"Code\" = " + Sql.Q(eq)) == 0, "Dernier document retiré : plus de pièce jointe");
                // Fichiers de test retirés du dossier partagé de SAP
                foreach (int atc in new[] { a1 })
                    foreach (Row r in AttachmentService.Files(atc))
                        try { System.IO.File.Delete(AttachmentService.FullPath(atc, r.Int("Line"))); } catch { }
                System.IO.Directory.Delete(dir, true);
            });

            Step("Lot 3 - alertes par la messagerie SAP", () =>
            {
                var s = SettingsService.Load();
                s.AlertUsers = "zzinconnu";
                Fails(() => SettingsService.Save(s), "Destinataire inconnu refusé", "inconnu");
                s.AlertUsers = DiCompany.UserCode + ", " + DiCompany.UserCode;
                s.AlertDays = 30;
                SettingsService.Save(s);
                Check(SettingsService.Load().AlertUsers == DiCompany.UserCode, "Destinataires enregistrés sans doublon (" + SettingsService.Load().AlertUsers + ")");

                var e = UdoData.Get(Obj.Equip, eq);
                e.Set("U_WarrEnd", DateTime.Today.AddDays(10));
                e.Update();
                string text = AlertService.DailyText(DateTime.Today, 30);
                Check(text.Contains("Garanties qui expirent") && text.Contains(eq), "Alerte du jour : garantie de " + eq + " qui expire sous 10 jours");
                int msg = AlertService.SendDaily(DateTime.Today, true);
                Check(msg > 0 && Sql.Exists("SELECT 1 FROM \"OAIB\" b JOIN \"OUSR\" u ON u.\"USERID\" = b.\"UserSign\" WHERE b.\"AlertCode\" = " + msg + " AND u.\"USER_CODE\" = " + Sql.Q(DiCompany.UserCode)),
                      "Message n° " + msg + " dans la messagerie de " + DiCompany.UserCode);
                Check(Sql.ScalarStr("SELECT \"Subject\" FROM \"OALR\" WHERE \"Code\" = " + msg).StartsWith("Maintenance - alertes du"), "Objet du message");
                Check(AlertService.SendDaily(DateTime.Today, false) == 0, "Pas de 2e envoi automatique le même jour");
                Check(SettingsService.Load().AlertDate == DateTime.Today, "Date du dernier envoi mémorisée");

                var n = UdoData.New(Obj.Notif);
                n.Set("U_Type", "M2"); n.Set("U_Status", "OSNO"); n.Set("U_Equip", eq); n.Set("U_Priority", "1"); n.Set("U_Breakdwn", "N");
                n.Set("U_Subject", "Panne urgente test " + S); n.Set("U_RepDate", DateTime.Today); n.Set("U_ReportBy", DiCompany.UserCode);
                int un = n.Add();
                int um = AlertService.NotifyUrgent(un);
                Check(um > 0 && Sql.ScalarStr("SELECT \"Subject\" FROM \"OALR\" WHERE \"Code\" = " + um).Contains("avis urgent"), "Avis de priorité 1 : message urgent n° " + um);
                var n2 = UdoData.New(Obj.Notif);
                n2.Set("U_Type", "M1"); n2.Set("U_Status", "OSNO"); n2.Set("U_Equip", eq); n2.Set("U_Priority", "3"); n2.Set("U_Breakdwn", "N"); n2.Set("U_Subject", "Demande normale test");
                Check(AlertService.NotifyUrgent(n2.Add()) == 0, "Avis normal : pas de message");
                NotificationService.Complete(un, DateTime.Today);
                e = UdoData.Get(Obj.Equip, eq);
                e.ClearDate("U_WarrEnd");
                e.Update();
            });

            // ================= Lot 4 : lien avec la production =================
            const string prodItem = "FCAS_CEL_1";
            string line = "TLN" + S, zone = "TLZ" + S, mA = "TMA" + S, mB = "TMB" + S, mC = "TMC" + S;
            int of1 = 0;
            Func<string, int> newOf = lineCode =>
            {
                var po = (ProductionOrders)c.GetBusinessObject(BoObjectTypes.oProductionOrders);
                po.ItemNo = prodItem; po.PlannedQuantity = 100; po.Warehouse = Whs;
                po.PostingDate = DateTime.Today; po.StartDate = DateTime.Today; po.DueDate = DateTime.Today.AddDays(2);
                if (lineCode != null) po.UserFields.Fields.Item("U_MNT_PLine").Value = lineCode;
                DiCompany.ThrowIfError(po.Add(), "OF de test");
                int entry = int.Parse(c.GetNewObjectKey());
                // Composants en sortie manuelle (pas de consommation automatique à l'entrée), puis lancement
                po = (ProductionOrders)c.GetBusinessObject(BoObjectTypes.oProductionOrders);
                po.GetByKey(entry);
                for (int i = 0; i < po.Lines.Count; i++) { po.Lines.SetCurrentLine(i); po.Lines.ProductionOrderIssueType = BoIssueMethod.im_Manual; }
                po.ProductionOrderStatus = BoProductionOrderStatusEnum.boposReleased;
                DiCompany.ThrowIfError(po.Update(), "Lancement de l'OF de test");
                return entry;
            };
            Action<int> closeOf = entry =>
            {
                var po = (ProductionOrders)c.GetBusinessObject(BoObjectTypes.oProductionOrders);
                if (po.GetByKey(entry) && po.ProductionOrderStatus == BoProductionOrderStatusEnum.boposReleased)
                {
                    po.ProductionOrderStatus = BoProductionOrderStatusEnum.boposClosed;
                    DiCompany.ThrowIfError(po.Update(), "Clôture de l'OF de test");
                }
            };
            Step("Lot 4 - ligne de production, machines, OF", () =>
            {
                Check(Sql.Exists("SELECT 1 FROM \"CUFD\" WHERE \"TableID\" = 'OWOR' AND \"AliasID\" = 'MNT_PLine'"), "Champ « Ligne de production » sur l'ordre de fabrication");
                Check(Sql.Exists("SELECT 1 FROM \"CUFD\" WHERE \"TableID\" = 'OITM' AND \"AliasID\" = 'MNT_PLine'"), "Champ « Ligne par défaut » sur l'article");
                var l = UdoData.New(Obj.FuncLoc);
                l.Set("Code", line); l.Set("Name", "Ligne d'embouteillage test"); l.Set("U_Active", "Y"); l.Set("U_IsLine", "Y"); l.Set("U_Parent", fl); l.Set("U_Whs", Whs);
                l.Add();
                var z = UdoData.New(Obj.FuncLoc);
                z.Set("Code", zone); z.Set("Name", "Zone étiquetage test"); z.Set("U_Active", "Y"); z.Set("U_IsLine", "N"); z.Set("U_Parent", line);
                z.Add();
                Action<string, string, string> machine = (code, name, loc) =>
                {
                    var e = UdoData.New(Obj.Equip);
                    e.Set("Code", code); e.Set("Name", name); e.Set("U_FuncLoc", loc); e.Set("U_Status", "A"); e.Set("U_Critic", "A");
                    if (code == mA)
                    {
                        var p = e.Lines(Db.EquipPts).Add();
                        p.SetProperty("U_Point", "CYC"); p.SetProperty("U_Descr", "Bouteilles remplies"); p.SetProperty("U_Unit", "u");
                        p.SetProperty("U_Counter", "Y"); p.SetProperty("U_ProdCnt", "Y");
                    }
                    if (code == mB)
                    {
                        var sp = e.Lines(Db.EquipParts).Add();
                        sp.SetProperty("U_ItemCode", Item); sp.SetProperty("U_ItemName", "Pièce critique test"); sp.SetProperty("U_Qty", 99999999.0);
                    }
                    e.Add();
                };
                machine(mA, "Remplisseuse test", line);
                machine(mB, "Boucheuse test", line);
                machine(mC, "Étiqueteuse test", zone);
                Check(ProductionService.LineOf(zone) == line && ProductionService.LineOfEquipment(mC) == line, "Machine d'un sous-poste rattachée à la ligne");
                Check(ProductionService.LineOf(fl) == "", "Site sans ligne de production");
                List<string> mach = ProductionService.Machines(line);
                Check(mach.Count == 3 && mach.Contains(mC), "3 machines sur la ligne (dont la zone)");

                // Les entrées de production antérieures ne sont pas comptées : point de départ
                ProductionService.SyncCounters();
                of1 = newOf(line);
                ProdOrderInfo info = ProductionService.Load(of1);
                Check(info != null && info.Line == line && info.Status == "R" && info.ItemCode == prodItem, "OF " + info?.DocNum + " lancé sur la ligne " + info?.Line);
                Check(ProductionService.CurrentOrderForEquipment(mC, DateTime.Today) == of1, "OF en cours retrouvé depuis une machine de la ligne");
                Check(ProductionService.CurrentOrderForEquipment(eq, DateTime.Today) == 0, "Machine hors ligne : pas d'OF");

                // Ligne par défaut de l'article : OF sans ligne saisie
                var it = (Items)c.GetBusinessObject(BoObjectTypes.oItems);
                it.GetByKey(prodItem);
                string oldLine = Convert.ToString(it.UserFields.Fields.Item("U_MNT_PLine").Value);
                it.UserFields.Fields.Item("U_MNT_PLine").Value = line;
                DiCompany.ThrowIfError(it.Update(), "Ligne par défaut de l'article");
                int of2 = newOf(null);
                Check(ProductionService.Load(of2).Line == line, "OF sans ligne : ligne par défaut de l'article reprise");
                closeOf(of2);
                it = (Items)c.GetBusinessObject(BoObjectTypes.oItems);
                it.GetByKey(prodItem);
                it.UserFields.Fields.Item("U_MNT_PLine").Value = oldLine;
                DiCompany.ThrowIfError(it.Update(), "Ligne par défaut de l'article remise");
            });

            Step("Lot 4 - compteurs alimentés par la production", () =>
            {
                var pc = UdoData.New(Obj.Plan);
                string pl = "TPP" + S;
                pc.Set("Code", pl); pc.Set("Name", "Révision remplisseuse 30 bouteilles test"); pc.Set("U_Type", "C"); pc.Set("U_Equip", mA);
                pc.Set("U_OrdType", "PM02"); pc.Set("U_Point", "CYC"); pc.Set("U_CycCount", 30.0); pc.Set("U_LeadCnt", 0.0);
                pc.Set("U_StartCnt", 0.0); pc.Set("U_NextCnt", 30.0); pc.Set("U_Basis", "P"); pc.Set("U_Active", "Y");
                pc.Add();
                Check(!PlanService.Overview(DateTime.Today).First(d => d.PlanCode == pl).IsDue, "Plan compteur pas encore dû (0 bouteille)");

                var rc = (Documents)c.GetBusinessObject(BoObjectTypes.oInventoryGenEntry);
                rc.DocDate = DateTime.Today;
                rc.Lines.BaseType = 202; rc.Lines.BaseEntry = of1; rc.Lines.Quantity = 40; rc.Lines.WarehouseCode = Whs;
                rc.Lines.TransactionType = BoTransactionTypeEnum.botrntComplete;
                DiCompany.ThrowIfError(rc.Add(), "Entrée de production de test");
                int n = ProductionService.SyncCounters();
                Check(n == 1 && Math.Abs(MeasurementService.CurrentCounter(mA, "CYC") - 40) < 0.001, "Entrée de 40 : compteur de la remplisseuse à 40 (" + n + " relevé)");
                Check(Sql.ScalarStr("SELECT TOP 1 \"U_Remarks\" FROM \"@MNT_MDOC\" WHERE \"U_Equip\" = " + Sql.Q(mA) + " ORDER BY \"Code\" DESC").StartsWith("Production : OF"),
                      "Relevé automatique commenté avec l'OF et l'entrée");
                Check(ProductionService.SyncCounters() == 0 && Math.Abs(MeasurementService.CurrentCounter(mA, "CYC") - 40) < 0.001, "Deuxième synchronisation : rien compté deux fois");
                Check(PlanService.Overview(DateTime.Today).First(d => d.PlanCode == pl).IsDue, "Plan « toutes les 30 bouteilles » devenu dû");
                Check(Math.Abs(MeasurementService.CurrentCounter(mB, "CYC")) < 0.001, "Machine sans compteur de production : rien");
                UdoData.Delete(Obj.Plan, pl);
            });

            int pNotif = 0, pOrder = 0;
            Step("Lot 4 - panne pendant l'OF, arrêt de ligne, perte, suivi", () =>
            {
                var n = UdoData.New(Obj.Notif);
                n.Set("U_Type", "M2"); n.Set("U_Status", "OSNO"); n.Set("U_Equip", mA); n.Set("U_FuncLoc", line); n.Set("U_Priority", "1");
                n.Set("U_Subject", "Bourrage remplisseuse test " + S); n.Set("U_RepDate", DateTime.Today); n.Set("U_ReportBy", DiCompany.UserCode);
                n.Set("U_Breakdwn", "Y"); n.Set("U_MalfStD", DateTime.Today); n.Set("U_MalfStT", DateTime.Today.AddHours(8));
                n.Set("U_MalfEnD", DateTime.Today); n.Set("U_MalfEnT", DateTime.Today.AddHours(10));
                n.Set("U_ProdOrd", of1); n.Set("U_LineStop", "Y"); n.Set("U_LostQty", 120.0);
                pNotif = n.Add();
                pOrder = NotificationService.CreateOrder(pNotif);
                Check(Sql.ScalarDbl("SELECT \"U_ProdOrd\" FROM \"@MNT_OORD\" WHERE \"DocEntry\" = " + pOrder) == of1, "Ordre de maintenance rattaché à l'OF (repris de l'avis)");
                var o = UdoData.Get(Obj.Order, pOrder);
                var cp = o.Lines(Db.OrderComps).Add();
                cp.SetProperty("U_ItemCode", Item); cp.SetProperty("U_ItemName", "Pièce test"); cp.SetProperty("U_Qty", 2.0); cp.SetProperty("U_Whs", Whs); cp.SetProperty("U_Proc", "S");
                o.Update();
                OrderService.Release(pOrder);
                int compLine = (int)Sql.ScalarDbl("SELECT \"LineId\" FROM \"@MNT_ORD2\" WHERE \"DocEntry\" = " + pOrder);
                OrderService.IssueComponents(pOrder, new[] { new Movement { LineId = compLine, Qty = 2 } }, DateTime.Today);

                // Intervention non rattachée sur une machine de la ligne pendant l'OF
                var d = new OrderDraft { OrdType = "PM01", Equip = mB, Subject = "Réglage boucheuse test " + S, Start = DateTime.Today };
                d.Ops.Add(new OpDraft { OpNo = "0010", Descr = "Réglage" });
                int other = OrderService.Create(d);
                Check(Sql.ScalarDbl("SELECT ISNULL(\"U_ProdOrd\", 0) FROM \"@MNT_OORD\" WHERE \"DocEntry\" = " + other) == 0, "Ordre sans OF");

                ProdOrderInfo info = ProductionService.Load(of1);
                List<Row> rows = Sql.Rows(ProductionService.InterventionsSql(info));
                Check(rows.Count == 3, "Suivi de l'OF : avis + ordre rattachés + ordre sur la ligne pendant l'OF (" + rows.Count + ")");
                Check(rows.Any(r => r.Str("Lien").StartsWith("Sur la ligne") && r.Str("Equip") == mB), "Intervention non rattachée signalée « sur la ligne pendant l'OF »");
                Check(Math.Abs(rows.Sum(r => r.Dbl("ArretProd")) - 2) < 0.01 && Math.Abs(rows.Sum(r => r.Dbl("QtePerdue")) - 120) < 0.001, "Arrêt de production 2 h, 120 perdues");
                Check(ProductionService.Summary(info).Contains("arrêt de production 2,0 h"), "Synthèse : " + ProductionService.Summary(info));
                List<Row> parts = Sql.Rows(ProductionService.PartsSql(info));
                Check(parts.Count == 1 && parts[0].Str("Article") == Item && Math.Abs(parts[0].Dbl("Qte") - 2) < 0.001, "Pièces consommées pendant l'OF : 2 × " + Item);
                Row rep = Sql.Rows(ReportService.View("PRD").Sql(new ReportFilter { From = DateTime.Today.AddDays(-1), To = DateTime.Today.AddDays(1) }))
                             .FirstOrDefault(r => r.Int("Key") == of1);
                Check(rep != null && rep.Int("NbAvis") == 1 && rep.Int("NbOrd") == 1 && Math.Abs(rep.Dbl("QtePerdue") - 120) < 0.001 && rep.Dbl("CoutMaint") > 0 &&
                      Math.Abs(rep.Dbl("ArretProd") - 2) < 0.01 && rep.Str("Ligne") == line,
                      "Rapport par OF : 1 avis, 1 ordre, 2 h, 120 perdues, coût " + rep?.Dbl("CoutMaint"));
                Check(Sql.ScalarStr("SELECT CAST(\"UserText\" AS NVARCHAR(4000)) FROM \"OALR\" WHERE \"Code\" = " + AlertService.NotifyUrgent(pNotif)).Contains("LIGNE DE PRODUCTION ARRÊTÉE"),
                      "Message urgent : OF et ligne arrêtée");
                OrderService.Cancel(other);
            });

            Step("Lot 4 - points à vérifier avant de lancer un OF", () =>
            {
                List<string> w = ProductionService.ReleaseWarnings(line);
                Check(w.Any(x => x.StartsWith(mB) && x.Contains("insuffisante")), "Pièce critique insuffisante signalée");
                Check(!w.Any(x => x.Contains("panne en cours")), "Panne terminée : pas signalée");
                var e = UdoData.Get(Obj.Equip, mC);
                e.Set("U_Status", "I");
                e.Update();
                var n = UdoData.New(Obj.Notif);
                n.Set("U_Type", "M2"); n.Set("U_Status", "OSNO"); n.Set("U_Equip", mA); n.Set("U_Priority", "2"); n.Set("U_Subject", "Fuite remplisseuse test");
                n.Set("U_Breakdwn", "Y"); n.Set("U_MalfStD", DateTime.Today);
                int open = n.Add();
                w = ProductionService.ReleaseWarnings(line);
                Check(w.Any(x => x.StartsWith(mC) && x.Contains("hors service")), "Machine hors service signalée");
                Check(w.Any(x => x.StartsWith(mA) && x.Contains("panne en cours")), "Panne en cours signalée");
                Check(ProductionService.ReleaseWarnings("ZZ" + S).Count == 0, "Poste sans machine : rien à signaler");
                Check(Sql.Rows(ProductionService.CriticalPartsSql(line)).First().Str("Alerte") == "Stock < quantité montée", "Pièces de la ligne : manque en tête");
                NotificationService.Complete(open, DateTime.Today);
                e = UdoData.Get(Obj.Equip, mC);
                e.Set("U_Status", "A");
                e.Update();
                OrderService.TechnicallyComplete(pOrder, DateTime.Today);
                closeOf(of1);
                Check(ProductionService.Load(of1).Status == "L" && ProductionService.CurrentOrderForEquipment(mA, DateTime.Today) == 0, "OF clôturé : plus d'OF en cours sur la ligne");
                List<Row> after = Sql.Rows(ProductionService.InterventionsSql(ProductionService.Load(of1)));
                Check(after.Any(r => r.Str("Nature") == "Avis" && r.Int("Key") == pNotif) && after.Any(r => r.Str("Nature") == "Ordre" && r.Int("Key") == pOrder), "Suivi d'un OF clôturé toujours consultable (" + after.Count + " ligne(s))");
            });

            Step("Rapports (exécution de toutes les requêtes)", () =>
            {
                var f = new ReportFilter { From = DateTime.Today.AddYears(-1), To = DateTime.Today.AddMonths(1) };
                foreach (ReportView v in ReportService.Views)
                {
                    try
                    {
                        int n = Sql.Rows(v.Sql(f)).Count;
                        f.Equip = eq;
                        int ne = Sql.Rows(v.Sql(f)).Count;
                        f.Equip = null;
                        Check(true, "Vue " + v.Code + " (" + v.Title + ") : " + n + " ligne(s), " + ne + " pour l'équipement de test");
                    }
                    catch (Exception ex) { Check(false, "Vue " + v.Code + " : " + ex.Message); }
                }
                Row k = Sql.First(ReportService.View("KPI").Sql(new ReportFilter { From = DateTime.Today.AddYears(-1).AddDays(1), To = DateTime.Today, Equip = eq }));
                Check(k != null && k.Int("NbPan") == 1 && k.Dbl("ArretH") >= 24, "KPI : 1 panne, arrêt ≥ 24 h (" + k?.Dbl("ArretH") + " h, MTTR " + k?.Dbl("MTTR") + ", dispo " + k?.Dbl("Dispo") + " %)");
                foreach (var sql in new[] { ReportService.EquipmentHistorySql(eq), ReportService.FuncLocContentSql(fl2), ReportService.PlanCallsSql(pT),
                                            ReportService.OrderConfirmationsSql(order), ReportService.OrderDocumentsSql(order),
                                            ReportService.ContractActivitySql(ctr), ReportService.EquipmentHistorySql(clim) })
                {
                    try { Check(Sql.Rows(sql).Count > 0, "Grille d'écran : " + sql.Substring(0, 50) + "..."); }
                    catch (Exception ex) { Check(false, "Grille d'écran : " + ex.Message); }
                }
                Check(Sql.Rows(ReportService.View("STR").Sql(f)).Any(r => r.Str("Element").Trim() == eq), "Structure technique : équipement sous sa ligne");
            });

            Console.WriteLine();
            Console.WriteLine("Résultat : " + _ok + " OK, " + _ko + " KO");
            c.Disconnect();
            return _ko == 0 ? 0 : 1;
        }
    }
}
