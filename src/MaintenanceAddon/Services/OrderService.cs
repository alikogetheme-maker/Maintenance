using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using SAPbobsCOM;
using MaintenanceAddon.Core;
using MaintenanceAddon.Models;

namespace MaintenanceAddon.Services
{
    internal sealed class OpDraft
    {
        public string OpNo = "";
        public string Descr = "";
        public string WorkCtr = "";
        public string CtrlKey = ControlKeys.Internal;
        public double PlanHrs;
        public int NbPers = 1;
        public double ExtCost;
        public string Vendor = "";
    }

    internal sealed class CompDraft
    {
        public string ItemCode = "";
        public string ItemName = "";
        public double Qty;
        public string Whs = "";
        public string OpNo = "";
        public string Proc = Procurement.Stock;
    }

    internal sealed class OrderDraft
    {
        public string OrdType = OrderTypes.Corrective;
        public string Equip = "";
        public string FuncLoc = "";
        public int NotifNo;
        public string PlanCode = "";
        public DateTime? CallDue;
        public string TaskList = "";
        public string Priority = "3";
        public string Subject = "";
        public string Descr = "";
        public string WorkCtr = "";
        public string OcrCode = "";
        public DateTime? Start;
        public DateTime? End;
        /// <summary>À immobiliser : coûts réglés sur un compte d'immobilisation à la clôture.</summary>
        public bool Capitalize;
        public List<OpDraft> Ops = new List<OpDraft>();
        public List<CompDraft> Comps = new List<CompDraft>();
    }

    /// <summary>Quantité à sortir / retourner sur une ligne de composant.</summary>
    internal sealed class Movement
    {
        public int LineId;
        public double Qty;
        public string Whs;
    }

    internal sealed class ConfirmationDraft
    {
        public int OpLineId;
        public DateTime Date = DateTime.Today;
        public int EmpId;
        public double Hours;
        public bool Final;
        public string Remarks = "";
    }

    /// <summary>
    /// Ordres de maintenance (IW31 / IW32 / IW41 / MIGO 261) : création,
    /// statuts système, sorties de stock, demandes d'achat, confirmations,
    /// calcul des coûts prévus et réels.
    /// </summary>
    internal static class OrderService
    {
        // =====================================================================
        // Lecture
        // =====================================================================

        public static string Status(int docEntry)
        {
            return Sql.ScalarStr("SELECT \"U_Status\" FROM " + Db.T(Db.Order) + " WHERE \"DocEntry\" = " + docEntry);
        }

        public static string DocNum(int docEntry)
        {
            string n = Sql.ScalarStr("SELECT \"DocNum\" FROM " + Db.T(Db.Order) + " WHERE \"DocEntry\" = " + docEntry);
            return n == "" ? docEntry.ToString(CultureInfo.InvariantCulture) : n;
        }

        /// <summary>
        /// Coût unitaire prévisionnel : coût moyen du magasin, sinon de l'article,
        /// sinon dernier prix d'achat (les articles FIFO n'ont pas de coût moyen article).
        /// </summary>
        public static double ItemCost(string itemCode, string whs)
        {
            return Sql.ScalarDbl(
                "SELECT COALESCE(NULLIF((SELECT w.\"AvgPrice\" FROM \"OITW\" w WHERE w.\"ItemCode\" = i.\"ItemCode\" AND w.\"WhsCode\" = " + Sql.Q(whs ?? "") + "), 0), " +
                "NULLIF(i.\"AvgPrice\", 0), i.\"LastPurPrc\", 0) FROM \"OITM\" i WHERE i.\"ItemCode\" = " + Sql.Q(itemCode));
        }

        /// <summary>Opérations et composants d'une gamme (IA05) pour pré-remplir un ordre.</summary>
        public static void LoadTaskList(string taskList, List<OpDraft> ops, List<CompDraft> comps)
        {
            foreach (Row r in Sql.Rows("SELECT * FROM " + Db.T(Db.TaskOps) + " WHERE \"Code\" = " + Sql.Q(taskList) + " ORDER BY \"U_OpNo\", \"LineId\""))
                ops.Add(new OpDraft
                {
                    OpNo = r.Str("U_OpNo"),
                    Descr = r.Str("U_Descr"),
                    WorkCtr = r.Str("U_WorkCtr"),
                    CtrlKey = r.Str("U_CtrlKey") == "" ? ControlKeys.Internal : r.Str("U_CtrlKey"),
                    PlanHrs = r.Dbl("U_PlanHrs"),
                    NbPers = Math.Max(1, r.Int("U_NbPers")),
                    ExtCost = r.Dbl("U_ExtCost"),
                    Vendor = r.Str("U_Vendor")
                });
            foreach (Row r in Sql.Rows("SELECT * FROM " + Db.T(Db.TaskComps) + " WHERE \"Code\" = " + Sql.Q(taskList) + " ORDER BY \"LineId\""))
                comps.Add(new CompDraft
                {
                    ItemCode = r.Str("U_ItemCode"),
                    ItemName = r.Str("U_ItemName"),
                    Qty = r.Dbl("U_Qty"),
                    Whs = r.Str("U_Whs"),
                    OpNo = r.Str("U_OpNo"),
                    Proc = r.Str("U_Proc") == "" ? Procurement.Stock : r.Str("U_Proc")
                });
        }

        // =====================================================================
        // Création
        // =====================================================================

        /// <summary>Crée un ordre « Créé » (CRTD) et calcule ses coûts prévus ; renvoie le DocEntry.</summary>
        public static int Create(OrderDraft d)
        {
            if (string.IsNullOrEmpty(d.Equip) && string.IsNullOrEmpty(d.FuncLoc))
                throw new InvalidOperationException("Un ordre doit porter sur un équipement ou un poste technique.");

            Settings s = SettingsService.Load();
            UdoData o = UdoData.New(Obj.Order);
            o.Set("U_OrdType", d.OrdType);
            o.Set("U_Status", OrderStatus.Created);
            o.Set("U_Equip", d.Equip);
            o.Set("U_FuncLoc", d.FuncLoc);
            if (d.NotifNo > 0)
                o.Set("U_NotifNo", d.NotifNo);
            o.Set("U_PlanCode", d.PlanCode);
            if (d.CallDue.HasValue)
                o.Set("U_CallDue", d.CallDue.Value);
            o.Set("U_TaskList", d.TaskList);
            o.Set("U_Priority", string.IsNullOrEmpty(d.Priority) ? "3" : d.Priority);
            o.Set("U_Subject", NotificationService.Truncate(d.Subject, 100));
            o.Set("U_Descr", d.Descr);
            o.Set("U_WorkCtr", d.WorkCtr);
            o.Set("U_OcrCode", d.OcrCode);
            o.Set("U_Respons", DiCompany.UserCode);
            DateTime start = d.Start ?? DateTime.Today;
            o.Set("U_StartDt", start.Date);
            o.Set("U_EndDt", (d.End ?? start).Date < start.Date ? start.Date : (d.End ?? start).Date);

            // Garantie / contrat de l'équipement pour ce type d'ordre
            ServiceContext ctx = string.IsNullOrEmpty(d.Equip) ? null : ServiceContext.For(d.Equip, start, d.OrdType);
            o.Set("U_UnderWar", ctx != null && ctx.UnderWarranty ? "Y" : "N");
            o.Set("U_Contract", ctx?.Contract?.Code ?? "");
            o.Set("U_Capital", d.Capitalize || d.OrdType == OrderTypes.Improvement ? "Y" : "N");
            if (d.Capitalize || d.OrdType == OrderTypes.Improvement)
                o.Set("U_CapAcct", s.CapitalAccount);

            GeneralDataCollection ops = o.Lines(Db.OrderOps);
            foreach (OpDraft op in d.Ops)
            {
                bool external = op.CtrlKey == ControlKeys.External;
                GeneralData l = ops.Add();
                l.SetProperty("U_OpNo", op.OpNo);
                l.SetProperty("U_Descr", NotificationService.Truncate(op.Descr, 100));
                l.SetProperty("U_WorkCtr", string.IsNullOrEmpty(op.WorkCtr) ? d.WorkCtr : op.WorkCtr);
                l.SetProperty("U_CtrlKey", op.CtrlKey);
                l.SetProperty("U_PlanHrs", op.PlanHrs);
                l.SetProperty("U_NbPers", op.NbPers);
                // Prestation couverte (garantie, contrat) : rien à payer en plus
                l.SetProperty("U_ExtCost", external && ctx != null && ctx.LaborCovered ? 0.0 : op.ExtCost);
                l.SetProperty("U_Vendor", external && string.IsNullOrEmpty(op.Vendor) && ctx != null ? ctx.DefaultVendor : op.Vendor);
                l.SetProperty("U_ActHrs", 0.0);
                l.SetProperty("U_Done", "N");
            }

            EquipmentInfo eq = EquipmentInfo.Load(d.Equip);
            string defaultWhs = eq != null && eq.Whs != "" ? eq.Whs : s.DefaultWarehouse;
            GeneralDataCollection comps = o.Lines(Db.OrderComps);
            foreach (CompDraft c in d.Comps)
            {
                GeneralData l = comps.Add();
                l.SetProperty("U_ItemCode", c.ItemCode);
                l.SetProperty("U_ItemName", NotificationService.Truncate(c.ItemName, 100));
                l.SetProperty("U_Qty", c.Qty);
                string whs = string.IsNullOrEmpty(c.Whs) ? defaultWhs : c.Whs;
                l.SetProperty("U_Whs", whs);
                l.SetProperty("U_OpNo", c.OpNo);
                l.SetProperty("U_Proc", c.Proc);
                l.SetProperty("U_UnitCost", ItemCost(c.ItemCode, whs));
                l.SetProperty("U_IssQty", 0.0);
            }

            return DiCompany.InTransaction(() =>
            {
                int docEntry = o.Add();
                RecalcCosts(docEntry);
                return docEntry;
            });
        }

        // =====================================================================
        // Statuts système
        // =====================================================================

        /// <summary>Lancement (CRTD → REL) : autorise sorties de stock et confirmations.</summary>
        public static void Release(int docEntry)
        {
            UdoData o = UdoData.Get(Obj.Order, docEntry);
            RequireStatus(o, OrderStatus.Created, "lancer");
            if (o.Lines(Db.OrderOps).Count == 0)
                throw new InvalidOperationException("Ajoutez au moins une opération avant de lancer l'ordre.");
            EquipmentInfo eq = EquipmentInfo.Load(o.Str("U_Equip"));
            if (eq != null && eq.Status == "S")
                throw new InvalidOperationException("L'équipement " + eq.Code + " est mis au rebut.");

            o.Set("U_Status", OrderStatus.Released);
            o.Set("U_RelDate", DateTime.Today);
            DiCompany.InTransaction(() =>
            {
                o.Update();
                NotificationService.OnOrderStatus((int)o.Dbl("U_NotifNo"), OrderStatus.Released, docEntry, DateTime.Today);
            });
        }

        /// <summary>Clôture technique (REL → TECO) : termine l'avis, met à jour le plan.</summary>
        public static void TechnicallyComplete(int docEntry, DateTime date)
        {
            UdoData o = UdoData.Get(Obj.Order, docEntry);
            RequireStatus(o, OrderStatus.Released, "clôturer techniquement");
            DateTime? start = o.Date("U_ActStart") ?? o.Date("U_RelDate");
            if (start.HasValue && date < start.Value)
                throw new InvalidOperationException("La date de fin ne peut pas précéder le début réel (" + start.Value.ToString("dd/MM/yyyy") + ").");

            o.Set("U_Status", OrderStatus.TechCompleted);
            o.Set("U_TecoDate", date);
            if (o.Date("U_ActStart") == null)
                o.Set("U_ActStart", start ?? date);
            o.Set("U_ActEnd", date);
            foreach (GeneralData op in UdoData.Each(o.Lines(Db.OrderOps)))
                op.SetProperty("U_Done", "Y");

            DiCompany.InTransaction(() =>
            {
                o.Update();
                NotificationService.OnOrderStatus((int)o.Dbl("U_NotifNo"), OrderStatus.TechCompleted, docEntry, date);
                PlanService.OnOrderCompleted(docEntry, o.Str("U_PlanCode"), date);
                RecalcCosts(docEntry);
            });
        }

        /// <summary>Annulation de la clôture technique (TECO → REL).</summary>
        public static void UndoTechnicalCompletion(int docEntry)
        {
            UdoData o = UdoData.Get(Obj.Order, docEntry);
            RequireStatus(o, OrderStatus.TechCompleted, "rouvrir");
            o.Set("U_Status", OrderStatus.Released);
            o.ClearDate("U_TecoDate");
            o.ClearDate("U_ActEnd");
            DiCompany.InTransaction(() =>
            {
                o.Update();
                int notif = (int)o.Dbl("U_NotifNo");
                if (notif > 0)
                    NotificationService.Reopen(notif);
                PlanService.OnOrderReopened(docEntry);
            });
        }

        /// <summary>Clôture (TECO → CLSD) : plus aucune imputation possible.</summary>
        public static void Close(int docEntry)
        {
            UdoData o = UdoData.Get(Obj.Order, docEntry);
            RequireStatus(o, OrderStatus.TechCompleted, "clôturer");

            List<Row> open = Sql.Rows(
                "SELECT 'Demande d''achat' AS \"Doc\", h.\"DocNum\" FROM \"PRQ1\" l JOIN \"OPRQ\" h ON h.\"DocEntry\" = l.\"DocEntry\" " +
                "WHERE l.\"" + DocFields.Order + "\" = " + docEntry + " AND l.\"LineStatus\" = 'O' AND h.\"CANCELED\" = 'N' " +
                "UNION ALL SELECT 'Commande d''achat', h.\"DocNum\" FROM \"POR1\" l JOIN \"OPOR\" h ON h.\"DocEntry\" = l.\"DocEntry\" " +
                "WHERE l.\"" + DocFields.Order + "\" = " + docEntry + " AND l.\"LineStatus\" = 'O' AND h.\"CANCELED\" = 'N'");
            if (open.Count > 0)
                throw new InvalidOperationException("Documents d'achat encore ouverts sur l'ordre : " +
                    string.Join(", ", open.Select(r => r.Str("Doc") + " " + r.Str("DocNum")).Distinct()) +
                    ".\nRéceptionnez-les et facturez-les (ou clôturez-les) avant de clôturer l'ordre.");

            bool capitalize = o.Str("U_Capital") == "Y";
            Settings s = SettingsService.Load();
            string capAcct = o.Str("U_CapAcct") != "" ? o.Str("U_CapAcct") : s.CapitalAccount;
            if (capitalize)
            {
                if (capAcct == "")
                    throw new InvalidOperationException("Ordre à immobiliser : renseignez le compte de règlement sur l'ordre ou le compte d'immobilisations en cours dans les paramètres.");
                SettingsService.CheckAccount(capAcct, "Compte de règlement");
            }

            DiCompany.InTransaction(() =>
            {
                RecalcCosts(docEntry);
                UdoData fresh = UdoData.Get(Obj.Order, docEntry);
                if (capitalize)
                {
                    double amount;
                    int je = Settle(docEntry, capAcct, s, fresh.Str("U_OcrCode"), out amount);
                    fresh.Set("U_SettJE", je);
                    fresh.Set("U_SettAmt", amount);
                    fresh.Set("U_CapAcct", capAcct);
                }
                fresh.Set("U_Status", OrderStatus.Closed);
                fresh.Set("U_CloseDt", DateTime.Today);
                fresh.Update();
            });
        }

        /// <summary>
        /// Coûts comptabilisés de l'ordre par compte de charges : sorties − retours,
        /// factures − avoirs (hors articles stockés), main-d'oeuvre comptabilisée.
        /// </summary>
        public static Dictionary<string, double> PostedCostsByAccount(int docEntry, Settings s)
        {
            string f = "l.\"" + DocFields.Order + "\" = " + docEntry;
            var result = new Dictionary<string, double>();
            Action<string, double> add = (acct, value) =>
            {
                if (string.IsNullOrEmpty(acct) || Math.Abs(value) < 0.005)
                    return;
                result.TryGetValue(acct, out double cur);
                result[acct] = cur + value;
            };
            string nonStock = " AND ISNULL((SELECT i.\"InvntItem\" FROM \"OITM\" i WHERE i.\"ItemCode\" = l.\"ItemCode\"), 'N') = 'N'";
            foreach (Row r in Sql.Rows(
                "SELECT l.\"AcctCode\" AS A, SUM(l.\"StockSum\") AS V FROM \"IGE1\" l JOIN \"OIGE\" h ON h.\"DocEntry\" = l.\"DocEntry\" WHERE h.\"CANCELED\" = 'N' AND " + f + " GROUP BY l.\"AcctCode\" " +
                "UNION ALL SELECT l.\"AcctCode\", -SUM(l.\"LineTotal\") FROM \"IGN1\" l JOIN \"OIGN\" h ON h.\"DocEntry\" = l.\"DocEntry\" WHERE h.\"CANCELED\" = 'N' AND " + f + " GROUP BY l.\"AcctCode\" " +
                "UNION ALL SELECT l.\"AcctCode\", SUM(l.\"LineTotal\") FROM \"PCH1\" l JOIN \"OPCH\" h ON h.\"DocEntry\" = l.\"DocEntry\" WHERE h.\"CANCELED\" = 'N' AND " + f + nonStock + " GROUP BY l.\"AcctCode\" " +
                "UNION ALL SELECT l.\"AcctCode\", -SUM(l.\"LineTotal\") FROM \"RPC1\" l JOIN \"ORPC\" h ON h.\"DocEntry\" = l.\"DocEntry\" WHERE h.\"CANCELED\" = 'N' AND " + f + nonStock + " GROUP BY l.\"AcctCode\""))
                add(r.Str("A"), r.Dbl("V"));
            // Main-d'oeuvre : seulement les montants réellement passés en comptabilité
            add(s.LaborExpenseAccount, Sql.ScalarDbl("SELECT ISNULL(SUM(\"U_Amount\"), 0) FROM " + Db.T(Db.Conf) +
                                                     " WHERE \"U_OrderNo\" = " + docEntry + " AND \"U_TransId\" > 0"));
            return result;
        }

        /// <summary>
        /// Règlement de l'ordre (KO88) : les coûts quittent les comptes de charges
        /// (avec le centre de coûts de l'ordre) pour le compte d'immobilisation.
        /// </summary>
        private static int Settle(int docEntry, string capAcct, Settings s, string ocr, out double amount)
        {
            Dictionary<string, double> costs = PostedCostsByAccount(docEntry, s);
            costs.Remove(capAcct);
            // Somme des montants arrondis : l'écriture reste équilibrée au centime
            amount = Math.Round(costs.Values.Sum(v => Math.Round(v, 2)), 2);
            if (Math.Abs(amount) < 0.005)
                return 0;

            string docNum = DocNum(docEntry);
            Company company = DiCompany.Instance;
            JournalEntries je = (JournalEntries)company.GetBusinessObject(BoObjectTypes.oJournalEntries);
            try
            {
                je.ReferenceDate = DateTime.Today;
                je.TaxDate = DateTime.Today;
                je.DueDate = DateTime.Today;
                je.Memo = NotificationService.Truncate("Règlement OM " + docNum + " - immobilisation", 50);
                je.Reference = NotificationService.Truncate("OM" + docNum, 100);

                je.Lines.AccountCode = capAcct;
                if (amount > 0) je.Lines.Debit = amount; else je.Lines.Credit = -amount;
                je.Lines.LineMemo = je.Memo;

                foreach (var c in costs)
                {
                    double v = Math.Round(c.Value, 2);
                    if (Math.Abs(v) < 0.005)
                        continue;
                    je.Lines.Add();
                    je.Lines.AccountCode = c.Key;
                    if (v > 0) je.Lines.Credit = v; else je.Lines.Debit = -v;
                    je.Lines.LineMemo = je.Memo;
                    SettingsService.SetCostingCode(je.Lines, s.Dimension, ocr);
                }
                DiCompany.ThrowIfError(je.Add(), "Écriture de règlement de l'ordre");
                return int.Parse(company.GetNewObjectKey(), CultureInfo.InvariantCulture);
            }
            finally
            {
                Marshal.ReleaseComObject(je);
            }
        }

        /// <summary>Annulation d'un ordre sans aucune imputation (CRTD ou REL).</summary>
        public static void Cancel(int docEntry)
        {
            UdoData o = UdoData.Get(Obj.Order, docEntry);
            string st = o.Str("U_Status");
            if (st != OrderStatus.Created && st != OrderStatus.Released)
                throw new InvalidOperationException("Seul un ordre créé ou lancé peut être annulé (statut actuel : " + OrderStatus.List.Caption(st) + ").");

            if (Sql.ScalarDbl("SELECT COUNT(*) FROM " + Db.T(Db.Conf) + " WHERE \"U_OrderNo\" = " + docEntry) > 0)
                throw new InvalidOperationException("Des temps ont été confirmés sur l'ordre : il ne peut plus être annulé (clôturez-le).");
            if (Sql.ScalarDbl("SELECT COUNT(*) FROM \"IGE1\" WHERE \"" + DocFields.Order + "\" = " + docEntry) > 0)
                throw new InvalidOperationException("Des pièces ont été sorties sur l'ordre : il ne peut plus être annulé (retournez-les puis clôturez-le).");
            if (Sql.ScalarDbl("SELECT COUNT(*) FROM \"PRQ1\" l JOIN \"OPRQ\" h ON h.\"DocEntry\" = l.\"DocEntry\" WHERE h.\"CANCELED\" = 'N' AND l.\"" + DocFields.Order + "\" = " + docEntry) > 0)
                throw new InvalidOperationException("Une demande d'achat existe pour l'ordre : annulez-la d'abord dans SAP.");

            o.Set("U_Status", OrderStatus.Cancelled);
            o.Set("U_CloseDt", DateTime.Today);
            DiCompany.InTransaction(() =>
            {
                o.Update();
                NotificationService.OnOrderStatus((int)o.Dbl("U_NotifNo"), OrderStatus.Cancelled, docEntry, DateTime.Today);
                PlanService.OnOrderCancelled(docEntry);
            });
        }

        private static void RequireStatus(UdoData o, string expected, string action)
        {
            string st = o.Str("U_Status");
            if (st != expected)
                throw new InvalidOperationException("Impossible de " + action + " l'ordre : statut " + OrderStatus.List.Caption(st) +
                                                    " (attendu : " + OrderStatus.List.Caption(expected) + ").");
        }

        // =====================================================================
        // Sorties et retours de stock (MIGO 261 / 262)
        // =====================================================================

        /// <summary>Sortie de stock des composants, imputée en charge sur l'ordre ; renvoie le N° de document.</summary>
        public static string IssueComponents(int docEntry, IList<Movement> moves, DateTime date)
        {
            return PostMovement(docEntry, moves, date, true);
        }

        /// <summary>Retour en stock de composants non utilisés, au coût de sortie.</summary>
        public static string ReturnComponents(int docEntry, IList<Movement> moves, DateTime date)
        {
            return PostMovement(docEntry, moves, date, false);
        }

        private static string PostMovement(int docEntry, IList<Movement> moves, DateTime date, bool issue)
        {
            moves = moves.Where(m => m.Qty > 0).ToList();
            if (moves.Count == 0)
                throw new InvalidOperationException("Saisissez au moins une quantité.");

            UdoData o = UdoData.Get(Obj.Order, docEntry);
            if (o.Str("U_Status") != OrderStatus.Released)
                throw new InvalidOperationException("Les mouvements de stock ne sont possibles que sur un ordre lancé (REL).");

            Settings s = SettingsService.Load();
            EquipmentInfo eq = EquipmentInfo.Load(o.Str("U_Equip"));
            string account = eq != null ? eq.ExpenseAccount(s) : s.ExpenseAccount;
            if (string.IsNullOrEmpty(account))
                throw new InvalidOperationException("Renseignez le compte de charges de maintenance dans Maintenance → Paramètres.");
            string ocr = o.Str("U_OcrCode");
            string docNum = DocNum(docEntry);
            GeneralDataCollection comps = o.Lines(Db.OrderComps);

            Company company = DiCompany.Instance;
            Documents doc = (Documents)company.GetBusinessObject(issue ? BoObjectTypes.oInventoryGenExit : BoObjectTypes.oInventoryGenEntry);
            try
            {
                doc.DocDate = date;
                doc.TaxDate = date;
                doc.Reference2 = NotificationService.Truncate("OM" + docNum, 11);
                doc.Comments = NotificationService.Truncate((issue ? "Sortie" : "Retour") + " de pièces - ordre de maintenance " + docNum + " - " + o.Str("U_Subject"), 254);
                doc.JournalMemo = NotificationService.Truncate((issue ? "Sortie maint. OM " : "Retour maint. OM ") + docNum, 50);

                bool first = true;
                foreach (Movement m in moves)
                {
                    GeneralData comp = UdoData.FindLine(comps, m.LineId);
                    if (comp == null)
                        throw new InvalidOperationException("Ligne de composant " + m.LineId + " introuvable.");
                    string item = UdoData.LineStr(comp, "U_ItemCode");
                    if (Sql.ScalarStr("SELECT \"InvntItem\" FROM \"OITM\" WHERE \"ItemCode\" = " + Sql.Q(item)) != "Y")
                        throw new InvalidOperationException("L'article " + item + " n'est pas géré en stock : passez par une demande d'achat.");
                    string whs = string.IsNullOrEmpty(m.Whs) ? UdoData.LineStr(comp, "U_Whs") : m.Whs;
                    if (string.IsNullOrEmpty(whs))
                        throw new InvalidOperationException("Magasin manquant pour l'article " + item + ".");

                    double issued = IssuedQty(docEntry, m.LineId);
                    if (!issue && m.Qty > issued + 0.000001)
                        throw new InvalidOperationException("Article " + item + " : retour de " + m.Qty + " supérieur à la quantité sortie (" + issued + ").");

                    if (!first)
                        doc.Lines.Add();
                    first = false;
                    doc.Lines.ItemCode = item;
                    doc.Lines.Quantity = m.Qty;
                    doc.Lines.WarehouseCode = whs;
                    doc.Lines.AccountCode = account;
                    if (!issue)
                    {
                        double unit = IssueUnitCost(docEntry, m.LineId);
                        if (unit > 0)
                            doc.Lines.UnitPrice = unit;
                    }
                    SettingsService.SetCostingCode(doc.Lines, s.Dimension, ocr);
                    doc.Lines.UserFields.Fields.Item(DocFields.Order).Value = docEntry;
                    doc.Lines.UserFields.Fields.Item(DocFields.Line).Value = m.LineId;
                }

                return DiCompany.InTransaction(() =>
                {
                    DiCompany.ThrowIfError(doc.Add(), issue ? "Sortie de stock" : "Retour en stock");
                    string newEntry = company.GetNewObjectKey();
                    if (o.Date("U_ActStart") == null)
                    {
                        o.Set("U_ActStart", date);
                        o.Update();
                    }
                    RecalcCosts(docEntry);
                    return Sql.ScalarStr("SELECT \"DocNum\" FROM \"" + (issue ? "OIGE" : "OIGN") + "\" WHERE \"DocEntry\" = " + Sql.Q(newEntry));
                });
            }
            finally
            {
                Marshal.ReleaseComObject(doc);
            }
        }

        /// <summary>Quantité nette sortie (sorties − retours) pour une ligne de composant.</summary>
        public static double IssuedQty(int docEntry, int lineId)
        {
            return Sql.ScalarDbl(
                "SELECT ISNULL((SELECT SUM(l.\"Quantity\") FROM \"IGE1\" l JOIN \"OIGE\" h ON h.\"DocEntry\" = l.\"DocEntry\" " +
                "  WHERE h.\"CANCELED\" = 'N' AND l.\"" + DocFields.Order + "\" = " + docEntry + " AND l.\"" + DocFields.Line + "\" = " + lineId + "), 0) - " +
                "ISNULL((SELECT SUM(l.\"Quantity\") FROM \"IGN1\" l JOIN \"OIGN\" h ON h.\"DocEntry\" = l.\"DocEntry\" " +
                "  WHERE h.\"CANCELED\" = 'N' AND l.\"" + DocFields.Order + "\" = " + docEntry + " AND l.\"" + DocFields.Line + "\" = " + lineId + "), 0)");
        }

        private static double IssueUnitCost(int docEntry, int lineId)
        {
            return Sql.ScalarDbl(
                "SELECT CASE WHEN SUM(l.\"Quantity\") = 0 THEN 0 ELSE SUM(l.\"StockSum\") / SUM(l.\"Quantity\") END " +
                "FROM \"IGE1\" l JOIN \"OIGE\" h ON h.\"DocEntry\" = l.\"DocEntry\" " +
                "WHERE h.\"CANCELED\" = 'N' AND l.\"" + DocFields.Order + "\" = " + docEntry + " AND l.\"" + DocFields.Line + "\" = " + lineId);
        }

        // =====================================================================
        // Demandes d'achat (composants non stockés, prestations externes)
        // =====================================================================

        /// <summary>Crée les demandes d'achat manquantes ; renvoie un message récapitulatif.</summary>
        public static string CreatePurchaseRequests(int docEntry)
        {
            UdoData o = UdoData.Get(Obj.Order, docEntry);
            string st = o.Str("U_Status");
            if (st != OrderStatus.Created && st != OrderStatus.Released)
                throw new InvalidOperationException("Les demandes d'achat se créent sur un ordre créé ou lancé.");

            Settings s = SettingsService.Load();
            EquipmentInfo eq = EquipmentInfo.Load(o.Str("U_Equip"));
            string account = eq != null ? eq.ExpenseAccount(s) : s.ExpenseAccount;
            string ocr = o.Str("U_OcrCode");
            string docNum = DocNum(docEntry);
            DateTime required = o.Date("U_StartDt") ?? DateTime.Today;
            if (required < DateTime.Today)
                required = DateTime.Today;

            // Garantie / contrat : la main-d'oeuvre (et les pièces si couvertes) ne se commandent pas
            bool warranty = o.Str("U_UnderWar") == "Y";
            ContractInfo contract = o.Str("U_Contract") == "" ? null : ContractService.Load(o.Str("U_Contract"));
            bool laborCovered = warranty || (contract != null && contract.CoversLabor);
            bool partsCovered = warranty || (contract != null && contract.CoversParts);
            string coverage = warranty ? "garantie" : contract != null ? "contrat " + contract.Code : "";

            var items = UdoData.Each(o.Lines(Db.OrderComps))
                .Where(l => UdoData.LineStr(l, "U_Proc") == Procurement.Purchase && UdoData.LineDbl(l, "U_PrEntry") == 0 && UdoData.LineDbl(l, "U_Qty") > 0)
                .ToList();
            var services = UdoData.Each(o.Lines(Db.OrderOps))
                .Where(l => UdoData.LineStr(l, "U_CtrlKey") == ControlKeys.External && UdoData.LineDbl(l, "U_PrEntry") == 0)
                .ToList();
            int skipped = 0;
            if (partsCovered)
            {
                skipped += items.Count;
                items.Clear();
            }
            if (laborCovered)
            {
                skipped += services.Count(l => UdoData.LineDbl(l, "U_ExtCost") == 0);
                services = services.Where(l => UdoData.LineDbl(l, "U_ExtCost") > 0).ToList();
            }
            if (items.Count == 0 && services.Count == 0)
                throw new InvalidOperationException(skipped > 0
                    ? "Rien à commander : les " + skipped + " ligne(s) externes sont couvertes par la " + coverage + ". Appelez directement le prestataire."
                    : "Aucune ligne à commander : composants « Achat » ou opérations externes sans demande d'achat.");
            foreach (GeneralData l in services)
                if (UdoData.LineStr(l, "U_Vendor") == "")
                {
                    ServiceContext ctx = ServiceContext.For(o.Str("U_Equip"), DateTime.Today);
                    if (ctx != null && ctx.DefaultVendor != "")
                        l.SetProperty("U_Vendor", ctx.DefaultVendor);
                }
            if (services.Count > 0 && string.IsNullOrEmpty(account))
                throw new InvalidOperationException("Renseignez le compte de charges de maintenance dans Maintenance → Paramètres (lignes de prestation).");

            var created = new List<string>();
            DiCompany.InTransaction(() =>
            {
                if (items.Count > 0)
                {
                    int pr = AddPurchaseRequest(BoDocumentTypes.dDocument_Items, docEntry, docNum, o.Str("U_Subject"), required, s, ocr, items, (line, l) =>
                    {
                        line.ItemCode = UdoData.LineStr(l, "U_ItemCode");
                        line.Quantity = UdoData.LineDbl(l, "U_Qty");
                        string whs = UdoData.LineStr(l, "U_Whs");
                        if (whs != "")
                            line.WarehouseCode = whs;
                    });
                    foreach (GeneralData l in items)
                        l.SetProperty("U_PrEntry", pr);
                    created.Add("articles n° " + PrDocNum(pr));
                }
                if (services.Count > 0)
                {
                    int pr = AddPurchaseRequest(BoDocumentTypes.dDocument_Service, docEntry, docNum, o.Str("U_Subject"), required, s, ocr, services, (line, l) =>
                    {
                        line.ItemDescription = NotificationService.Truncate("OM " + docNum + " op. " + UdoData.LineStr(l, "U_OpNo") + " - " + UdoData.LineStr(l, "U_Descr"), 100);
                        line.AccountCode = account;
                        double cost = UdoData.LineDbl(l, "U_ExtCost");
                        if (cost > 0)
                            line.LineTotal = cost;
                        string vendor = UdoData.LineStr(l, "U_Vendor");
                        if (vendor != "")
                            line.LineVendor = vendor;
                    });
                    foreach (GeneralData l in services)
                        l.SetProperty("U_PrEntry", pr);
                    created.Add("prestations n° " + PrDocNum(pr));
                }
                o.Update();
            });
            return "Demande(s) d'achat créée(s) : " + string.Join(", ", created) + "." +
                   (skipped > 0 ? "\n" + skipped + " ligne(s) couverte(s) par la " + coverage + " non commandée(s)." : "");
        }

        private static int AddPurchaseRequest(BoDocumentTypes type, int docEntry, string docNum, string subject, DateTime required,
                                              Settings s, string ocr, List<GeneralData> lines, Action<Document_Lines, GeneralData> fill)
        {
            Company company = DiCompany.Instance;
            Documents pr = (Documents)company.GetBusinessObject(BoObjectTypes.oPurchaseRequest);
            try
            {
                pr.DocType = type;
                pr.DocDate = DateTime.Today;
                pr.RequriedDate = required;
                pr.ReqType = 12; // demandeur = utilisateur SAP
                pr.Requester = DiCompany.UserCode;
                pr.Comments = NotificationService.Truncate("Ordre de maintenance " + docNum + " - " + subject, 254);
                bool first = true;
                foreach (GeneralData l in lines)
                {
                    if (!first)
                        pr.Lines.Add();
                    first = false;
                    fill(pr.Lines, l);
                    pr.Lines.RequiredDate = required;
                    SettingsService.SetCostingCode(pr.Lines, s.Dimension, ocr);
                    pr.Lines.UserFields.Fields.Item(DocFields.Order).Value = docEntry;
                    pr.Lines.UserFields.Fields.Item(DocFields.Line).Value = UdoData.LineId(l);
                }
                DiCompany.ThrowIfError(pr.Add(), "Création de la demande d'achat");
                return int.Parse(company.GetNewObjectKey(), CultureInfo.InvariantCulture);
            }
            finally
            {
                Marshal.ReleaseComObject(pr);
            }
        }

        private static string PrDocNum(int prEntry)
        {
            return Sql.ScalarStr("SELECT \"DocNum\" FROM \"OPRQ\" WHERE \"DocEntry\" = " + prEntry);
        }

        // =====================================================================
        // Confirmations de temps (IW41)
        // =====================================================================

        public static void Confirm(int docEntry, ConfirmationDraft c)
        {
            if (c.Hours == 0)
                throw new InvalidOperationException("Saisissez un nombre d'heures (négatif pour corriger une confirmation).");
            if (c.Date > DateTime.Today)
                throw new InvalidOperationException("La date de confirmation ne peut pas être dans le futur.");

            UdoData o = UdoData.Get(Obj.Order, docEntry);
            if (o.Str("U_Status") != OrderStatus.Released)
                throw new InvalidOperationException("Les temps se confirment sur un ordre lancé (REL).");
            GeneralData op = UdoData.FindLine(o.Lines(Db.OrderOps), c.OpLineId);
            if (op == null)
                throw new InvalidOperationException("Opération introuvable sur l'ordre.");

            double already = Sql.ScalarDbl("SELECT ISNULL(SUM(\"U_Hours\"), 0) FROM " + Db.T(Db.Conf) +
                                           " WHERE \"U_OrderNo\" = " + docEntry + " AND \"U_OpLine\" = " + c.OpLineId);
            if (already + c.Hours < -0.000001)
                throw new InvalidOperationException("La correction rendrait le temps confirmé négatif (déjà confirmé : " + already + " h).");

            string workCtr = UdoData.LineStr(op, "U_WorkCtr");
            if (workCtr == "")
                workCtr = o.Str("U_WorkCtr");
            Row wc = Sql.First("SELECT \"U_Rate\", \"U_AbsAcct\" FROM " + Db.T(Db.WorkCtr) + " WHERE \"Code\" = " + Sql.Q(workCtr));
            double rate = wc?.Dbl("U_Rate") ?? 0;
            double amount = Math.Round(c.Hours * rate, 2);

            string empName = c.EmpId > 0
                ? Sql.ScalarStr("SELECT ISNULL(\"firstName\", '') + ' ' + ISNULL(\"lastName\", '') FROM \"OHEM\" WHERE \"empID\" = " + c.EmpId)
                : "";
            Settings s = SettingsService.Load();
            string docNum = DocNum(docEntry);

            DiCompany.InTransaction(() =>
            {
                int transId = 0;
                if (s.PostLabor && amount != 0)
                {
                    string absorption = wc != null && wc.Str("U_AbsAcct") != "" ? wc.Str("U_AbsAcct") : s.LaborAbsorptionAccount;
                    if (s.LaborExpenseAccount == "" || absorption == "")
                        throw new InvalidOperationException("Comptes de main-d'oeuvre non paramétrés (Maintenance → Paramètres).");
                    transId = PostLaborEntry(c.Date, docNum, UdoData.LineStr(op, "U_OpNo"), amount, s.LaborExpenseAccount, absorption, s.Dimension, o.Str("U_OcrCode"));
                }

                UserTable t = DiCompany.Instance.UserTables.Item(Db.Conf);
                try
                {
                    string code = Sql.NextCode(Db.Conf);
                    t.Code = code;
                    t.Name = code;
                    Fields f = t.UserFields.Fields;
                    f.Item("U_OrderNo").Value = docEntry;
                    f.Item("U_OpLine").Value = c.OpLineId;
                    f.Item("U_OpNo").Value = UdoData.LineStr(op, "U_OpNo");
                    f.Item("U_ConfDate").Value = c.Date;
                    f.Item("U_EmpId").Value = c.EmpId;
                    f.Item("U_EmpName").Value = NotificationService.Truncate(empName.Trim(), 100);
                    f.Item("U_WorkCtr").Value = workCtr;
                    f.Item("U_Hours").Value = c.Hours;
                    f.Item("U_Rate").Value = rate;
                    f.Item("U_Amount").Value = amount;
                    f.Item("U_Final").Value = c.Final ? "Y" : "N";
                    f.Item("U_Remarks").Value = NotificationService.Truncate(c.Remarks, 200);
                    f.Item("U_TransId").Value = transId;
                    f.Item("U_User").Value = DiCompany.UserCode;
                    DiCompany.ThrowIfError(t.Add(), "Enregistrement de la confirmation");
                }
                finally
                {
                    Marshal.ReleaseComObject(t);
                }

                if (c.Final)
                    op.SetProperty("U_Done", "Y");
                DateTime? actStart = o.Date("U_ActStart");
                if (actStart == null || c.Date < actStart.Value)
                    o.Set("U_ActStart", c.Date);
                o.Update();
                RecalcCosts(docEntry);
            });
        }

        /// <summary>Écriture d'imputation de la main-d'oeuvre : charge de l'ordre / produit d'imputation de l'atelier.</summary>
        private static int PostLaborEntry(DateTime date, string docNum, string opNo, double amount, string expense, string absorption, int dim, string ocr)
        {
            Company company = DiCompany.Instance;
            JournalEntries je = (JournalEntries)company.GetBusinessObject(BoObjectTypes.oJournalEntries);
            try
            {
                je.ReferenceDate = date;
                je.TaxDate = date;
                je.DueDate = date;
                je.Memo = NotificationService.Truncate("Main-d'oeuvre OM " + docNum + " op. " + opNo, 50);
                je.Reference = NotificationService.Truncate("OM" + docNum, 100);

                double value = Math.Abs(amount);
                bool normal = amount > 0;

                je.Lines.AccountCode = expense;
                if (normal) je.Lines.Debit = value; else je.Lines.Credit = value;
                je.Lines.LineMemo = je.Memo;
                SettingsService.SetCostingCode(je.Lines, dim, ocr);

                je.Lines.Add();
                je.Lines.AccountCode = absorption;
                if (normal) je.Lines.Credit = value; else je.Lines.Debit = value;
                je.Lines.LineMemo = je.Memo;

                DiCompany.ThrowIfError(je.Add(), "Écriture de main-d'oeuvre");
                return int.Parse(company.GetNewObjectKey(), CultureInfo.InvariantCulture);
            }
            finally
            {
                Marshal.ReleaseComObject(je);
            }
        }

        // =====================================================================
        // Coûts (prévus / réels)
        // =====================================================================

        /// <summary>
        /// Recalcule depuis la base : quantités sorties et heures réalisées par
        /// ligne, coûts prévus (heures × taux, quantités × coût, prestations) et
        /// réels (confirmations, sorties − retours, factures fournisseurs).
        /// </summary>
        public static void RecalcCosts(int docEntry)
        {
            UdoData o = UdoData.Get(Obj.Order, docEntry);
            string mainWc = o.Str("U_WorkCtr");
            string ordFilter = " = " + docEntry;

            double plLab = 0, plExt = 0, plMat = 0;
            foreach (GeneralData op in UdoData.Each(o.Lines(Db.OrderOps)))
            {
                int lineId = UdoData.LineId(op);
                if (UdoData.LineStr(op, "U_CtrlKey") == ControlKeys.External)
                {
                    plExt += UdoData.LineDbl(op, "U_ExtCost");
                }
                else
                {
                    string wc = UdoData.LineStr(op, "U_WorkCtr");
                    double rate = Sql.ScalarDbl("SELECT \"U_Rate\" FROM " + Db.T(Db.WorkCtr) + " WHERE \"Code\" = " + Sql.Q(wc == "" ? mainWc : wc));
                    plLab += UdoData.LineDbl(op, "U_PlanHrs") * rate;
                }
                double act = Sql.ScalarDbl("SELECT ISNULL(SUM(\"U_Hours\"), 0) FROM " + Db.T(Db.Conf) +
                                           " WHERE \"U_OrderNo\"" + ordFilter + " AND \"U_OpLine\" = " + lineId);
                op.SetProperty("U_ActHrs", act);
            }

            foreach (GeneralData comp in UdoData.Each(o.Lines(Db.OrderComps)))
            {
                double unit = UdoData.LineDbl(comp, "U_UnitCost");
                if (unit == 0)
                {
                    unit = ItemCost(UdoData.LineStr(comp, "U_ItemCode"), UdoData.LineStr(comp, "U_Whs"));
                    comp.SetProperty("U_UnitCost", unit);
                }
                plMat += UdoData.LineDbl(comp, "U_Qty") * unit;
                comp.SetProperty("U_IssQty", IssuedQty(docEntry, UdoData.LineId(comp)));
            }

            double acLab = Sql.ScalarDbl("SELECT ISNULL(SUM(\"U_Amount\"), 0) FROM " + Db.T(Db.Conf) + " WHERE \"U_OrderNo\"" + ordFilter);
            double acMat = Sql.ScalarDbl(
                "SELECT ISNULL((SELECT SUM(l.\"StockSum\") FROM \"IGE1\" l JOIN \"OIGE\" h ON h.\"DocEntry\" = l.\"DocEntry\" WHERE h.\"CANCELED\" = 'N' AND l.\"" + DocFields.Order + "\"" + ordFilter + "), 0) - " +
                "ISNULL((SELECT SUM(l.\"LineTotal\") FROM \"IGN1\" l JOIN \"OIGN\" h ON h.\"DocEntry\" = l.\"DocEntry\" WHERE h.\"CANCELED\" = 'N' AND l.\"" + DocFields.Order + "\"" + ordFilter + "), 0)");
            // Factures fournisseurs : prestations et articles non stockés (les articles
            // stockés passent par le stock et sont comptés à la sortie).
            double acExt = Sql.ScalarDbl(
                "SELECT ISNULL((SELECT SUM(l.\"LineTotal\") FROM \"PCH1\" l JOIN \"OPCH\" h ON h.\"DocEntry\" = l.\"DocEntry\" LEFT JOIN \"OITM\" i ON i.\"ItemCode\" = l.\"ItemCode\" " +
                "  WHERE h.\"CANCELED\" = 'N' AND l.\"" + DocFields.Order + "\"" + ordFilter + " AND ISNULL(i.\"InvntItem\", 'N') = 'N'), 0) - " +
                "ISNULL((SELECT SUM(l.\"LineTotal\") FROM \"RPC1\" l JOIN \"ORPC\" h ON h.\"DocEntry\" = l.\"DocEntry\" LEFT JOIN \"OITM\" i ON i.\"ItemCode\" = l.\"ItemCode\" " +
                "  WHERE h.\"CANCELED\" = 'N' AND l.\"" + DocFields.Order + "\"" + ordFilter + " AND ISNULL(i.\"InvntItem\", 'N') = 'N'), 0)");

            o.Set("U_PlLab", Math.Round(plLab, 2));
            o.Set("U_PlMat", Math.Round(plMat, 2));
            o.Set("U_PlExt", Math.Round(plExt, 2));
            o.Set("U_AcLab", Math.Round(acLab, 2));
            o.Set("U_AcMat", Math.Round(acMat, 2));
            o.Set("U_AcExt", Math.Round(acExt, 2));
            o.Update();
        }
    }
}
