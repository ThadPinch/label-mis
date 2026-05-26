using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Pages.Invoices;

[Authorize(Policy = TransactionPolicies.InvoicesRead)]
public class IndexModel(InvoiceService invoiceService, LabelsMisDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public InvoiceStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? CustomerId { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? FromDate { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? ToDate { get; set; }
    [BindProperty(SupportsGet = true)] public string? AgingBucket { get; set; }
    [BindProperty(SupportsGet = true, Name = "page")] public int PageNumber { get; set; } = 1;

    public Services.Models.PagedResult<InvoiceListItem> Result { get; private set; } = null!;
    public bool CanEdit => User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Accounting);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Result = await invoiceService.ListAsync(
            Search, Status, CustomerId, FromDate, ToDate, AgingBucket, null, PageNumber, 25, cancellationToken);

        ViewData["CustomerOptions"] = await db.Customers.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync(cancellationToken);
    }
}
