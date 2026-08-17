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

    [BindProperty(SupportsGet = true)] public string? Sort { get; set; }

    [BindProperty(SupportsGet = true, Name = "pageNumber")] public int PageNumber { get; set; } = 1;

    public PagedResult<JobListItem> Result { get; private set; } = null!;

    public bool CanAdvance { get; private set; }

    /// <summary>Whether the advance button opens the record popup. Stages with nothing to
    /// record (pre-press) override this to keep the instant one-click advance.</summary>
    public virtual bool UseActionModal => true;

    /// <summary>Pre-press shows whether each job's imposition has been run; later stages don't need it.</summary>
    public virtual bool ShowImpositionColumn => false;

    public IReadOnlyDictionary<JobStatus, int> StageCounts { get; private set; } =
        new Dictionary<JobStatus, int>();

    public virtual async Task OnGetAsync(CancellationToken cancellationToken)
    {
        CanAdvance = CanUserAdvance();
        await LoadStageCountsAsync(cancellationToken);
        Result = await jobService.ListAsync(
            Search, Stage, null, null, null, null, null, Sort ?? "due", PageNumber, 50, cancellationToken);
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

    /// <summary>Serves the job action popup body, fetched into the shared modal shell.</summary>
    public async Task<IActionResult> OnGetActionPanelAsync(Guid jobId, CancellationToken cancellationToken)
    {
        if (!CanUserAdvance())
        {
            return Forbid();
        }

        var panel = await jobService.GetActionPanelAsync(jobId, cancellationToken);
        return panel is null ? NotFound() : Partial("_JobActionPanel", panel);
    }

    /// <summary>The popup's main submit: roll claim + counts/time, then advance the job.</summary>
    public async Task<IActionResult> OnPostRecordAdvanceAsync(
        Guid jobId,
        Guid operationId,
        int goodCount,
        int wasteCount,
        decimal actualMinutes,
        decimal downtimeMinutes,
        DowntimeReasonCode? downtimeReason,
        string? rollBarcode,
        decimal? consumedLf,
        string? returnUrl,
        CancellationToken cancellationToken)
    {
        if (!CanUserAdvance())
        {
            return Forbid();
        }

        try
        {
            await jobService.RecordAndAdvanceAsync(
                jobId, operationId, goodCount, wasteCount, downtimeMinutes, downtimeReason,
                actualMinutes, rollBarcode, consumedLf, cancellationToken);
        }
        catch (Exception ex)
        {
            TempData["JobActionError"] = ex.Message;
        }

        return RedirectToReturnUrl(returnUrl);
    }

    /// <summary>Completes one finishing task from the popup (time + optional laminate claim).</summary>
    public async Task<IActionResult> OnPostCompleteTaskAsync(
        Guid operationId,
        decimal actualMinutes,
        string? rollBarcode,
        decimal? consumedLf,
        string? returnUrl,
        CancellationToken cancellationToken)
    {
        if (!CanUserAdvance())
        {
            return Forbid();
        }

        try
        {
            await jobService.CompleteFinishingTaskAsync(
                operationId, actualMinutes, rollBarcode, consumedLf, cancellationToken);
        }
        catch (Exception ex)
        {
            TempData["JobActionError"] = ex.Message;
        }

        return RedirectToReturnUrl(returnUrl);
    }

    /// <summary>Back to the list the popup was opened from, so filters/sort/page survive.</summary>
    protected IActionResult RedirectToReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToStage();

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
        if (!string.IsNullOrWhiteSpace(Sort))
        {
            query = query.Add("Sort", Sort);
        }
        if (PageNumber > 1)
        {
            query = query.Add("pageNumber", PageNumber.ToString());
        }

        return Redirect($"{Request.Path}{query}");
    }

    protected bool CanUserAdvance() =>
        User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Scheduler) || User.IsInRole(AppRoles.Operator);
}
