using LabelsMis.Infrastructure.Identity;

namespace LabelsMis.Web.Authorization;

public static class TransactionPolicies
{
    public const string EstimatesRead = "EstimatesRead";
    public const string EstimatesEdit = "EstimatesEdit";
    public const string EstimatesAdmin = "EstimatesAdmin";
    public const string ProductsRead = "ProductsRead";
    public const string ProductsEdit = "ProductsEdit";
    public const string SalesOrdersRead = "SalesOrdersRead";
    public const string SalesOrdersEdit = "SalesOrdersEdit";
    public const string JobsRead = "JobsRead";
    public const string JobsEdit = "JobsEdit";
    public const string JobsOperator = "JobsOperator";
    public const string JobsAdmin = "JobsAdmin";
    public const string InventoryRead = "InventoryRead";
    public const string InventoryEdit = "InventoryEdit";
    public const string ShippingRead = "ShippingRead";
    public const string ShippingEdit = "ShippingEdit";
    public const string InvoicesRead = "InvoicesRead";
    public const string InvoicesEdit = "InvoicesEdit";
    public const string AdminOverride = "AdminOverride";

    /// <summary>Finance reports pages: requires the Accounting role explicitly.</summary>
    public const string FinanceReports = "FinanceReports";

    public static readonly string[] EstimatesReadRoles = [AppRoles.Admin, AppRoles.Estimator, AppRoles.Csr];
    public static readonly string[] EstimatesEditRoles = [AppRoles.Admin, AppRoles.Estimator];
    public static readonly string[] EstimatesAdminRoles = [AppRoles.Admin];
    public static readonly string[] ProductsReadRoles = [AppRoles.Admin, AppRoles.Estimator, AppRoles.Csr];
    public static readonly string[] ProductsEditRoles = [AppRoles.Admin, AppRoles.Estimator];
    public static readonly string[] SalesOrdersReadRoles = [AppRoles.Admin, AppRoles.Estimator, AppRoles.Csr];
    public static readonly string[] SalesOrdersEditRoles = [AppRoles.Admin, AppRoles.Csr, AppRoles.Estimator];
    public static readonly string[] JobsReadRoles = [AppRoles.Admin, AppRoles.Scheduler, AppRoles.Estimator, AppRoles.Csr, AppRoles.Operator];
    public static readonly string[] JobsEditRoles = [AppRoles.Admin, AppRoles.Scheduler];
    public static readonly string[] JobsOperatorRoles = [AppRoles.Admin, AppRoles.Operator];
    public static readonly string[] JobsAdminRoles = [AppRoles.Admin];
    public static readonly string[] InventoryReadRoles = [AppRoles.Admin, AppRoles.Scheduler, AppRoles.Operator];
    public static readonly string[] InventoryEditRoles = [AppRoles.Admin, AppRoles.Scheduler];
    public static readonly string[] ShippingReadRoles = [AppRoles.Admin, AppRoles.Shipping, AppRoles.Csr];
    public static readonly string[] ShippingEditRoles = [AppRoles.Admin, AppRoles.Shipping];
    // Finance-tab pages are gated strictly on the Accounting role: users (including admins)
    // must have it explicitly assigned to see or edit anything financial.
    public static readonly string[] InvoicesReadRoles = [AppRoles.Accounting];
    public static readonly string[] InvoicesEditRoles = [AppRoles.Accounting];
    public static readonly string[] FinanceReportsRoles = [AppRoles.Accounting];
    public static readonly string[] AdminOverrideRoles = [AppRoles.Admin];
}
