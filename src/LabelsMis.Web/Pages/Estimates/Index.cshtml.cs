using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Estimates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using LabelsMis.Infrastructure.Persistence;

namespace LabelsMis.Web.Pages.Estimates;

[Authorize(Policy = TransactionPolicies.EstimatesRead)]
public class IndexModel(
    EstimateService estimateService,
    LabelsMisDbContext db,
    UserManager<ApplicationUser> userManager) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public EstimateStatus? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? CustomerId { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? SalesRepId { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    [BindProperty(SupportsGet = true, Name = "pageNumber")]
    public int PageNumber { get; set; } = 1;

    public Services.Models.PagedResult<EstimateListItem> Result { get; private set; } = null!;
    public bool CanEdit { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        CanEdit = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Estimator);
        Result = await estimateService.ListAsync(
            Search, Status, CustomerId, SalesRepId, FromDate, ToDate, Sort, PageNumber, 25, cancellationToken);

        ViewData["CustomerOptions"] = await db.Customers.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString()))
            .ToListAsync(cancellationToken);

        var users = await userManager.Users.OrderBy(u => u.Email).ToListAsync(cancellationToken);
        ViewData["SalesRepOptions"] = users.Select(u => new SelectListItem(
            u.Email ?? u.UserName ?? u.Id.ToString(), u.Id.ToString())).ToList();
    }
}
