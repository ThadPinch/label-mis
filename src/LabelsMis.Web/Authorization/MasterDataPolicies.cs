using LabelsMis.Infrastructure.Identity;

namespace LabelsMis.Web.Authorization;

public static class MasterDataPolicies
{
    public const string Read = "MasterDataRead";
    public const string Edit = "MasterDataEdit";

    public static readonly string[] ReadRoles = [AppRoles.Admin, AppRoles.Estimator, AppRoles.Csr];
    public static readonly string[] EditRoles = [AppRoles.Admin, AppRoles.Estimator];
}
