using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Jobs;
using LabelsMis.Web.Services.Models;
using LabelsMis.Web.Services.Shipments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Production;

[Authorize(Policy = TransactionPolicies.ShippingRead)]
public class ShippingModel(ShipmentService shipmentService, JobService jobService) : PageModel, IProductionStageNav
{
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "page")] public int PageNumber { get; set; } = 1;

    public PagedResult<ReadyToShipOrder> Result { get; private set; } = null!;

    public JobStatus Stage => JobStatus.Rewound;
    public IReadOnlyDictionary<JobStatus, int> StageCounts { get; private set; } =
        new Dictionary<JobStatus, int>();

    public bool CanShip => User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Shipping);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        StageCounts = await jobService.GetStatusCountsAsync(
            ProductionStages.All.Select(s => s.Status), cancellationToken);
        Result = await shipmentService.GetReadyToShipAsync(Search, PageNumber, 25, cancellationToken);
    }
}
