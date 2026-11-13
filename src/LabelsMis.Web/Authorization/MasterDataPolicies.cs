using LabelsMis.Infrastructure.Identity;

namespace LabelsMis.Web.Authorization;

public static class MasterDataPolicies
{
    public const string Read = "MasterDataRead";
    public const string Edit = "MasterDataEdit";

    /// <summary>Open a customer's page: everyone who reads master data, plus Accounting, who links
    /// customers to QuickBooks from it (the rest of the form stays read-only for them).</summary>
    public const string CustomerView = "CustomerView";

    /// <summary>Search, link, unlink and create QuickBooks customers, and the Match customers page.</summary>
    public const string QuickBooksLink = "CustomerQuickBooksLink";

    public static readonly string[] ReadRoles = [AppRoles.Admin, AppRoles.Estimator, AppRoles.Csr];
    public static readonly string[] EditRoles = [AppRoles.Admin, AppRoles.Estimator];
    public static readonly string[] CustomerViewRoles = [AppRoles.Admin, AppRoles.Estimator, AppRoles.Csr, AppRoles.Accounting];
    public static readonly string[] QuickBooksLinkRoles = [AppRoles.Admin, AppRoles.Estimator, AppRoles.Accounting];
}
