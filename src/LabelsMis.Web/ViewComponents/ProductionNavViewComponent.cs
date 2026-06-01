using LabelsMis.Domain.Enums;
using LabelsMis.Web.Pages.Production;
using LabelsMis.Web.Services.Jobs;
using Microsoft.AspNetCore.Mvc;

namespace LabelsMis.Web.ViewComponents;

/// <summary>Renders the production stage nav links with a live job count badge on each.</summary>
public class ProductionNavViewComponent(JobService jobService) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        IReadOnlyDictionary<JobStatus, int> counts;
        try
        {
            counts = await jobService.GetStatusCountsAsync(ProductionStages.All.Select(s => s.Status));
        }
        catch
        {
            // Never let a count query break the layout; fall back to no badges.
            counts = new Dictionary<JobStatus, int>();
        }

        return View(counts);
    }
}
