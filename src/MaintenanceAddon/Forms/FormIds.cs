namespace MaintenanceAddon.Forms
{
    /// <summary>Identifiants des menus et des écrans de l'add-on.</summary>
    internal static class FormIds
    {
        // ---- Menus (Modules → Maintenance) --------------------------------
        public const string MenuRoot = "MNTM_ROOT";
        public const string MenuMasterData = "MNTM_MD";
        public const string MenuFuncLoc = "MNTM_FLOC";
        public const string MenuEquip = "MNTM_EQP";
        public const string MenuTaskList = "MNTM_TSK";
        public const string MenuWorkCtr = "MNTM_WCT";
        public const string MenuCatalog = "MNTM_CAT";
        public const string MenuEqCat = "MNTM_ECT";
        public const string MenuContract = "MNTM_CTR";
        public const string MenuShip = "MNTM_SHP";
        public const string MenuNotif = "MNTM_NOT";
        public const string MenuOrder = "MNTM_ORD";
        public const string MenuMeasure = "MNTM_MEA";
        public const string MenuPreventive = "MNTM_PRV";
        public const string MenuPlan = "MNTM_PLN";
        public const string MenuSched = "MNTM_SCH";
        public const string MenuReports = "MNTM_REP";
        public const string MenuSetup = "MNTM_SET";

        // Rapports : UID de menu = "MNTM_R_" + code de vue de ListForm
        public const string MenuReportPrefix = "MNTM_R_";

        // ---- Écrans (FormType = UniqueID) ----------------------------------
        public const string FuncLocForm = "MNT_FFLOC";
        public const string EquipForm = "MNT_FEQP";
        public const string TaskListForm = "MNT_FTSK";
        public const string PlanForm = "MNT_FPLN";
        public const string NotifForm = "MNT_FNOT";
        public const string OrderForm = "MNT_FORD";
        public const string ConfForm = "MNT_FCNF";
        public const string GoodsForm = "MNT_FGDS";
        public const string MeasureForm = "MNT_FMEA";
        public const string SchedForm = "MNT_FSCH";
        public const string ListForm = "MNT_FLST";
        public const string SetupForm = "MNT_FSET";
        public const string ContractForm = "MNT_FCTR";
        public const string ShipForm = "MNT_FSHP";
        public const string SerialForm = "MNT_FSER";
        public const string SparePartsForm = "MNT_FSPR";
        public const string ProductionForm = "MNT_FPRD";
        public const string MenuProd = "MNTM_PRD";
    }
}
