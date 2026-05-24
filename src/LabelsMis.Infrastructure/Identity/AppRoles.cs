namespace LabelsMis.Infrastructure.Identity;

public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Estimator = "Estimator";
    public const string Csr = "CSR";
    public const string Scheduler = "Scheduler";
    public const string Operator = "Operator";
    public const string Shipping = "Shipping";
    public const string Accounting = "Accounting";

    public static readonly IReadOnlyList<string> All =
    [
        Admin,
        Estimator,
        Csr,
        Scheduler,
        Operator,
        Shipping,
        Accounting
    ];
}
