using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.SalesOrders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Pages.SalesOrders;

[Authorize(Policy = TransactionPolicies.SalesOrdersRead)]
public class IndexModel(SalesOrderService salesOrderService, LabelsMisDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public SalesOrderStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? CustomerId { get; set; }
    [BindProperty(SupportsGet = true, Name = "page")] public int PageNumber { get; set; } = 1;

    public Services.Models.PagedResult<SalesOrderListItem> Result { get; private set; } = null!;
    public bool CanEdit { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        CanEdit = User.IsInRole(Infrastructure.Identity.AppRoles.Admin)
            || User.IsInRole(Infrastructure.Identity.AppRoles.Csr)
            || User.IsInRole(Infrastructure.Identity.AppRoles.Estimator);
        Result = await salesOrderService.ListAsync(Search, Status, CustomerId, null, null, null, PageNumber, 25, cancellationToken);
        ViewData["CustomerOptions"] = await db.Customers.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync(cancellationToken);
    }
}
