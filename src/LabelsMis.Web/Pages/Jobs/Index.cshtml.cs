using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Jobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Pages.Jobs;

[Authorize(Policy = TransactionPolicies.JobsRead)]
public class IndexModel(JobService jobService, LabelsMisDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string StatusFilter { get; set; } = "live";
    [BindProperty(SupportsGet = true)] public Guid? PressId { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? CustomerId { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? DueFrom { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? DueTo { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? ScheduledDate { get; set; }
    [BindProperty(SupportsGet = true)] public string? Sort { get; set; }
    [BindProperty(SupportsGet = true, Name = "pageNumber")] public int PageNumber { get; set; } = 1;

    public Services.Models.PagedResult<JobListItem> Result { get; private set; } = null!;
    public bool CanEdit { get; private set; }
    public bool CanAdvance { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        CanEdit = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Scheduler);
        CanAdvance = CanUserAdvance();

        JobStatus? singleStatus = null;
        IReadOnlyCollection<JobStatus>? includeStatuses = null;
        if (string.Equals(StatusFilter, "all", StringComparison.OrdinalIgnoreCase))
        {
            // No status filter — show everything.
        }
        else if (Enum.TryParse<JobStatus>(StatusFilter, ignoreCase: true, out var parsed))
        {
            singleStatus = parsed;
        }
        else
        {
            // Default "live" view: everything except Shipped/Closed.
            includeStatuses = JobService.LiveStatuses;
        }

        Result = await jobService.ListAsync(
            Search, singleStatus, PressId, CustomerId, DueFrom, DueTo, ScheduledDate, Sort, PageNumber, 25,
            cancellationToken, includeStatuses);

        ViewData["CustomerOptions"] = await db.Customers.AsNoTracking()
            .Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync(cancellationToken);

        ViewData["PressOptions"] = await db.Presses.AsNoTracking()
            .Where(p => p.IsActive).OrderBy(p => p.Name)
            .Select(p => new SelectListItem(p.Name, p.Id.ToString())).ToListAsync(cancellationToken);
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

    private IActionResult RedirectToReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToPage();

    private bool CanUserAdvance() =>
        User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Scheduler) || User.IsInRole(AppRoles.Operator);
}
