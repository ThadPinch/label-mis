using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Jobs;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Pages.Production;
using LabelsMis.Web.Pages.Shared;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.Jobs;
using LabelsMis.Web.Services.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Pages.Jobs;

/// <summary>One stage column on the floor board.</summary>
public record FloorColumn(JobStatus Status, string Label, string StagePage, IReadOnlyList<FloorJob> Jobs, int LateCount);

/// <summary>What a job card / list row needs besides the job.</summary>
public record FloorCardModel(FloorJob Job, bool CanAdvance, bool CanShip, DateOnly Today);

public record FloorDueModel(DateOnly? DueDate, DateOnly Today, DateOnly? ScheduledForDate = null);

/// <summary>
/// The jobs floor: every live job and where it is right now — a board with one column per
/// production stage (cards show the station/step, who's on it, the route and the due flag), or the
/// same jobs as a sortable list. Live views load every live job so the find box filters instantly
/// in the browser; Shipped / Closed / All are history and page through the server.
/// </summary>
[Authorize(Policy = TransactionPolicies.JobsRead)]
public class IndexModel(JobService jobService, JobFloorService floorService, ClientClock clock, LabelsMisDbContext db) : PageModel
{
    public const string ViewCookie = "ls_jobs_view";
    public const int HistoryPageSize = 50;

    /// <summary>Board column order: the floor's flow left to right, then work that's off site.</summary>
    public static readonly IReadOnlyList<JobStatus> BoardStages =
        [JobStatus.PrePress, JobStatus.Queued, JobStatus.Printed, JobStatus.Finished, JobStatus.Rewound, JobStatus.Outsourced];

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string StatusFilter { get; set; } = "live";
    [BindProperty(SupportsGet = true)] public Guid? PressId { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? CustomerId { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? DueFrom { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? DueTo { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? ScheduledDate { get; set; }
    [BindProperty(SupportsGet = true)] public string? Sort { get; set; }
    [BindProperty(SupportsGet = true, Name = "pageNumber")] public int PageNumber { get; set; } = 1;

    /// <summary>"board" or "list"; remembered in a cookie when chosen.</summary>
    [BindProperty(SupportsGet = true, Name = "view")] public string? View { get; set; }

    public bool CanEdit { get; private set; }
    public bool CanAdvance { get; private set; }
    public bool CanShip { get; private set; }

    public DateOnly Today { get; private set; }

    /// <summary>Shipped / Closed / All: paged from the server, text search applied there.</summary>
    public bool IsHistory { get; private set; }

    public bool IsBoard { get; private set; }

    /// <summary>The one live stage being shown, or null for all live stages.</summary>
    public JobStatus? SelectedStage { get; private set; }

    public IReadOnlyList<FloorJob> Jobs { get; private set; } = [];

    public IReadOnlyList<FloorColumn> Columns { get; private set; } = [];

    public PagedResult<FloorJob>? History { get; private set; }

    public IReadOnlyDictionary<JobStatus, int> StageCounts { get; private set; } = new Dictionary<JobStatus, int>();

    public int LiveCount => JobService.LiveStatuses.Sum(s => StageCounts.GetValueOrDefault(s));

    public IReadOnlyList<SelectListItem> CustomerOptions { get; private set; } = [];

    public IReadOnlyList<SelectListItem> PressOptions { get; private set; } = [];

    public bool HasMoreFilters => DueFrom is not null || DueTo is not null || ScheduledDate is not null;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        CanEdit = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Scheduler);
        CanAdvance = CanUserAdvance();
        CanShip = CanUserShip();
        Today = clock.Today;

        ResolveView();

        var baseFilter = new FloorQuery(
            CustomerId: CustomerId,
            PressId: PressId,
            DueFrom: DueFrom,
            DueTo: DueTo,
            ScheduledDate: ScheduledDate,
            Sort: Sort);

        StageCounts = await floorService.CountByStatusAsync(baseFilter, cancellationToken);

        if (IsHistory)
        {
            IReadOnlyCollection<JobStatus>? statuses =
                Enum.TryParse<JobStatus>(StatusFilter, ignoreCase: true, out var historyStatus) ? [historyStatus] : null;
            History = await floorService.ListAsync(
                baseFilter with { Search = Search, Statuses = statuses }, PageNumber, HistoryPageSize, cancellationToken);
            Jobs = History.Items;
        }
        else
        {
            // Every live job under the filters (the find box narrows them in the browser).
            var live = await floorService.ListAsync(
                baseFilter with { Statuses = JobService.LiveStatuses.ToList() }, 1, JobFloorService.MaxJobs, cancellationToken);
            var lateByStage = live.Items
                .Where(j => JobDue.Classify(j.DueDate, Today) == DueUrgency.Overdue)
                .GroupBy(j => j.Status)
                .ToDictionary(g => g.Key, g => g.Count());

            Jobs = SelectedStage is { } stage ? live.Items.Where(j => j.Status == stage).ToList() : live.Items;
            Columns = BoardStages
                .Where(s => SelectedStage is null || s == SelectedStage)
                .Select(s => new FloorColumn(
                    s,
                    JobFloorLocator.StageLabel(s),
                    ProductionStages.All.FirstOrDefault(p => p.Status == s)?.Page ?? "/Jobs/Index",
                    JobDue.OrderForFloor(live.Items.Where(j => j.Status == s), j => j.DueDate, j => j.JobNumber).ToList(),
                    lateByStage.GetValueOrDefault(s)))
                .ToList();
            LateByStage = lateByStage;
        }

        CustomerOptions = await db.Customers.AsNoTracking()
            .Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync(cancellationToken);

        PressOptions = await db.Presses.AsNoTracking()
            .Where(p => p.IsActive).OrderBy(p => p.Name)
            .Select(p => new SelectListItem(p.Name, p.Id.ToString())).ToListAsync(cancellationToken);
    }

    /// <summary>Overdue jobs per live stage (live views only).</summary>
    public IReadOnlyDictionary<JobStatus, int> LateByStage { get; private set; } = new Dictionary<JobStatus, int>();

    /// <summary>Query values for links that keep the current filters, with overrides applied.</summary>
    public Dictionary<string, string?> RouteWith(params (string Key, string? Value)[] overrides)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["view"] = IsBoard ? "board" : "list",
            ["StatusFilter"] = StatusFilter,
            ["Search"] = Search,
            ["CustomerId"] = CustomerId?.ToString(),
            ["PressId"] = PressId?.ToString(),
            ["DueFrom"] = DueFrom?.ToString("yyyy-MM-dd"),
            ["DueTo"] = DueTo?.ToString("yyyy-MM-dd"),
            ["ScheduledDate"] = ScheduledDate?.ToString("yyyy-MM-dd"),
            ["Sort"] = Sort
        };
        foreach (var (key, value) in overrides)
        {
            values[key] = value;
        }

        return values;
    }

    public string Href(params (string Key, string? Value)[] overrides) =>
        "?" + string.Join("&", RouteWith(overrides)
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value!)}"));

    private void ResolveView()
    {
        IsHistory = string.Equals(StatusFilter, "all", StringComparison.OrdinalIgnoreCase)
            || string.Equals(StatusFilter, nameof(JobStatus.Shipped), StringComparison.OrdinalIgnoreCase)
            || string.Equals(StatusFilter, nameof(JobStatus.Closed), StringComparison.OrdinalIgnoreCase);

        if (!IsHistory && Enum.TryParse<JobStatus>(StatusFilter, ignoreCase: true, out var stage)
            && JobService.LiveStatuses.Contains(stage))
        {
            SelectedStage = stage;
        }
        else if (!IsHistory)
        {
            StatusFilter = "live";
        }

        var chosen = View?.Trim().ToLowerInvariant();
        if (chosen is "board" or "list")
        {
            Response.Cookies.Append(ViewCookie, chosen, new CookieOptions
            {
                Path = "/",
                MaxAge = TimeSpan.FromDays(365),
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                HttpOnly = true
            });
        }
        else
        {
            chosen = Request.Cookies[ViewCookie];
        }

        IsBoard = !IsHistory && chosen != "list";
    }

    /// <summary>Serves the job action popup body, fetched into the shared modal shell.</summary>
    public async Task<IActionResult> OnGetActionPanelAsync(Guid jobId, CancellationToken cancellationToken)
    {
        if (!CanUserAdvance())
        {
            return Forbid();
        }

        var panel = await jobService.GetActionPanelAsync(jobId, cancellationToken);
        return panel is null ? NotFound() : Partial("_JobActionPanel", panel with { CanShip = CanUserShip() });
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
        bool recordOnly,
        CancellationToken cancellationToken)
    {
        if (!CanUserAdvance())
        {
            return Forbid();
        }

        try
        {
            var shipment = await jobService.RecordAndAdvanceAsync(
                jobId, operationId, goodCount, wasteCount, downtimeMinutes, downtimeReason,
                actualMinutes, rollBarcode, consumedLf, advance: !recordOnly, cancellationToken: cancellationToken);
            if (recordOnly)
            {
                TempData["JobActionMessage"] = "Counts recorded. The job is marked shipped when its shipment is recorded.";
            }
            else if (shipment is not null)
            {
                TempData["JobActionMessage"] = $"Marked shipped and recorded on shipment {shipment.ShipmentNumber} (no packages).";
            }
        }
        catch (Exception ex)
        {
            TempData["JobActionError"] = ex.Message;
        }

        return RedirectToReturnUrl(returnUrl);
    }

    /// <summary>
    /// Completes one finishing task from the popup (time + optional laminate claim). The popup
    /// posts these in place (job-action-modal.js), so a fetch request gets the refreshed panel
    /// back — remaining tasks, or all done — instead of a redirect that would close the popup.
    /// A plain form post keeps the redirect back to the list.
    /// </summary>
    public async Task<IActionResult> OnPostCompleteTaskAsync(
        Guid jobId,
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

        string? error = null;
        try
        {
            await jobService.CompleteFinishingTaskAsync(
                operationId, actualMinutes, rollBarcode, consumedLf, cancellationToken);
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        if (Request.IsFetch())
        {
            // Always the finishing-task view: completing the last task already moved the job
            // on, and the operator should still see the list with everything ticked off.
            var panel = await jobService.GetActionPanelAsync(jobId, cancellationToken, JobStatus.Printed);
            return panel is null ? NotFound() : Partial("_JobActionPanel", panel with { Error = error });
        }

        if (error is not null)
        {
            TempData["JobActionError"] = error;
        }

        return RedirectToReturnUrl(returnUrl);
    }

    private IActionResult RedirectToReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToPage();

    private bool CanUserAdvance() =>
        User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Scheduler) || User.IsInRole(AppRoles.Operator);

    private bool CanUserShip() =>
        User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Shipping);
}
