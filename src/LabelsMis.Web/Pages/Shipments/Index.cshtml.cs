using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Shipments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Pages.Shipments;

[Authorize(Policy = TransactionPolicies.ShippingRead)]
public class IndexModel(ShipmentService shipmentService, LabelsMisDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public ShipmentStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? CustomerId { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? ShipFrom { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? ShipTo { get; set; }
    [BindProperty(SupportsGet = true, Name = "page")] public int PageNumber { get; set; } = 1;

    public Services.Models.PagedResult<ShipmentListItem> Result { get; private set; } = null!;
    public bool CanEdit => User.IsInRole("Admin") || User.IsInRole("Shipping");

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Result = await shipmentService.ListAsync(Search, Status, CustomerId, ShipFrom, ShipTo, null, PageNumber, 25, cancellationToken);
        ViewData["CustomerOptions"] = await db.Customers.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync(cancellationToken);
    }
}
