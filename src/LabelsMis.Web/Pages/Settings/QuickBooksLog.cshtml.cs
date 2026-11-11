using LabelsMis.Infrastructure.Identity;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.QuickBooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Settings;

/// <summary>Setup → QuickBooks → Activity log: every attempt LabelShop made against QuickBooks,
/// newest first, with Intuit's trace id for support tickets.</summary>
[Authorize(Roles = AppRoles.Admin + "," + AppRoles.Accounting)]
public class QuickBooksLogModel(QuickBooksSyncLogger log, ClientClock clock) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "page")] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true, Name = "failures")] public bool FailuresOnly { get; set; }

    public QuickBooksLogPage Log { get; private set; } = new([], 1, 1, 0, false);

    public string Local(DateTime utc) => clock.ToLocal(utc).ToString("MMM d, yyyy h:mm:ss tt");

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Log = await log.GetPageAsync(PageNumber, FailuresOnly, cancellationToken);
    }
}
