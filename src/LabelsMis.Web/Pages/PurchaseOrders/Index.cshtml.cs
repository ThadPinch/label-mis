using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.PurchaseOrders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Pages.PurchaseOrders;

[Authorize(Policy = TransactionPolicies.InventoryRead)]
public class IndexModel(PurchaseOrderService purchaseOrderService, LabelsMisDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public PurchaseOrderStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? SupplierId { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? ExpectedFrom { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? ExpectedTo { get; set; }
    [BindProperty(SupportsGet = true, Name = "pageNumber")] public int PageNumber { get; set; } = 1;

    public Services.Models.PagedResult<PurchaseOrderListItem> Result { get; private set; } = null!;
    public bool CanEdit => User.IsInRole("Admin") || User.IsInRole("Scheduler");

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Result = await purchaseOrderService.ListAsync(
            Search, Status, SupplierId, ExpectedFrom, ExpectedTo, null, PageNumber, 25, cancellationToken);

        ViewData["SupplierOptions"] = await db.Suppliers.AsNoTracking()
            .Where(s => s.IsActive).OrderBy(s => s.Name)
            .Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToListAsync(cancellationToken);
    }
}
