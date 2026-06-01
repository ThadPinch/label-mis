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
    [BindProperty(SupportsGet = true, Name = "page")] public int PageNumber { get; set; } = 1;

    public Services.Models.PagedResult<JobListItem> Result { get; private set; } = null!;
    public bool CanEdit { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        CanEdit = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Scheduler);

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
}
