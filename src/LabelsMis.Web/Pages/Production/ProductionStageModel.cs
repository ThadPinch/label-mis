using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Jobs;
using LabelsMis.Web.Services.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Production;

/// <summary>
/// Shared logic for the production stage list pages (Pre-press, Press, Finishing, Shipping).
/// Each derived page fixes the job status it lists and the status its advance button moves jobs to.
/// </summary>
[Authorize(Policy = TransactionPolicies.JobsRead)]
public abstract class ProductionStageModel(JobService jobService) : PageModel, IProductionStageNav
{
    protected JobService JobService => jobService;

    /// <summary>The job status this page lists.</summary>
    public abstract JobStatus Stage { get; }

    /// <summary>The status the advance button moves a job to, or null for a read-only stage.</summary>
    public abstract JobStatus? NextStatus { get; }

    public abstract string StageTitle { get; }

    public abstract string AdvanceLabel { get; }

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")] public int PageNumber { get; set; } = 1;

    public PagedResult<JobListItem> Result { get; private set; } = null!;

    public bool CanAdvance { get; private set; }

    public IReadOnlyDictionary<JobStatus, int> StageCounts { get; private set; } =
        new Dictionary<JobStatus, int>();

    public virtual async Task OnGetAsync(CancellationToken cancellationToken)
    {
        CanAdvance = CanUserAdvance();
        await LoadStageCountsAsync(cancellationToken);
        Result = await jobService.ListAsync(
            Search, Stage, null, null, null, null, null, "due", PageNumber, 50, cancellationToken);
    }

    public async Task<IActionResult> OnPostAdvanceAsync(Guid jobId, CancellationToken cancellationToken)
    {
        if (!CanUserAdvance())
        {
            return Forbid();
        }

        if (NextStatus is { } next)
        {
            await jobService.AdvanceJobStatusAsync(jobId, next, cancellationToken);
        }

        return RedirectToStage();
    }

    protected async Task LoadStageCountsAsync(CancellationToken cancellationToken)
    {
        CanAdvance = CanUserAdvance();
        StageCounts = await jobService.GetStatusCountsAsync(
            ProductionStages.All.Select(s => s.Status), cancellationToken);
    }

    /// <summary>
    /// Redirect back to this stage's list, preserving the filter/page. Built by hand because
    /// "page" is a reserved Razor Pages route token and collides with RedirectToPage.
    /// </summary>
    protected IActionResult RedirectToStage()
    {
        var query = QueryString.Empty;
        if (!string.IsNullOrWhiteSpace(Search))
        {
            query = query.Add("Search", Search);
        }
        if (PageNumber > 1)
        {
            query = query.Add("page", PageNumber.ToString());
        }

        return Redirect($"{Request.Path}{query}");
    }

    protected bool CanUserAdvance() =>
        User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Scheduler) || User.IsInRole(AppRoles.Operator);
}
