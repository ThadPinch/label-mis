using System.Text.Json;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages;

[Authorize]
public class IndexModel(DashboardService dashboardService, IAuthorizationService authorizationService) : PageModel
{
    private static readonly JsonSerializerOptions ChartJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly int[] AllowedRanges = [30, 90, 180, 365];

    [BindProperty(SupportsGet = true)]
    public int Range { get; set; } = 180;

    public DashboardData Data { get; private set; } = null!;
    public bool CanSales { get; private set; }
    public bool CanJobs { get; private set; }
    public bool CanEstimates { get; private set; }
    public bool CanFinance { get; private set; }
    public bool CanInventory { get; private set; }
    public bool CanShipping { get; private set; }

    public string RangeLabel => Range switch
    {
        30 => "last 30 days",
        90 => "last 3 months",
        180 => "last 6 months",
        _ => "last 12 months"
    };

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (!AllowedRanges.Contains(Range))
        {
            Range = 180;
        }

        CanSales = await Allowed(TransactionPolicies.SalesOrdersRead);
        CanJobs = await Allowed(TransactionPolicies.JobsRead);
        CanEstimates = await Allowed(TransactionPolicies.EstimatesRead);
        // Finance widgets moved to the finance reports page; the shop dashboard stays operational.
        CanFinance = false;
        CanInventory = await Allowed(TransactionPolicies.InventoryRead);
        CanShipping = await Allowed(TransactionPolicies.ShippingRead);

        Data = await dashboardService.GetAsync(
            Range, CanSales, CanJobs, CanEstimates, CanFinance, CanInventory, CanShipping, cancellationToken);
    }

    /// <summary>Chart payload consumed by dashboard.js. Web defaults escape HTML-sensitive chars.</summary>
    public string ChartJson => JsonSerializer.Serialize(new
    {
        rangeLabel = RangeLabel,
        jobPipeline = Data.Jobs?.Pipeline,
        estimateFunnel = Data.Estimates?.Funnel,
        stockLevels = Data.Inventory?.StockLevels,
        workloadByDepartment = Data.Workload?.ByDepartment
    }, ChartJsonOptions);

    private async Task<bool> Allowed(string policy) =>
        (await authorizationService.AuthorizeAsync(User, policy)).Succeeded;
}
