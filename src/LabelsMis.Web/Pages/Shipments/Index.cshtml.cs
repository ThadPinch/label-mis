using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Models;
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
    // Which tab is shown on load; preserved across pager/filter reloads via the `tab` query value.
    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "ready";

    // Ready to Ship tab
    [BindProperty(SupportsGet = true)] public string? ReadySearch { get; set; }
    [BindProperty(SupportsGet = true, Name = "readyPage")] public int ReadyPage { get; set; } = 1;

    // History tab
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public ShipmentStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? CustomerId { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? ShipFrom { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? ShipTo { get; set; }
    [BindProperty(SupportsGet = true, Name = "historyPage")] public int HistoryPage { get; set; } = 1;

    public PagedResult<ReadyToShipOrder> ReadyResult { get; private set; } = null!;
    public PagedResult<ShipmentListItem> Result { get; private set; } = null!;

    public bool CanShip => User.IsInRole("Admin") || User.IsInRole("Shipping");
    public bool ReadyActive => !string.Equals(Tab, "history", StringComparison.OrdinalIgnoreCase);

    public async Task<IActionResult> OnPostMarkShippedAsync(Guid salesOrderId, bool isPickup, CancellationToken cancellationToken)
    {
        if (!CanShip) return Forbid();

        try
        {
            var shipment = await shipmentService.MarkShippedAsync(salesOrderId, cancellationToken);
            TempData["ShipMessage"] = isPickup
                ? $"Order marked picked up — shipment {shipment.ShipmentNumber} recorded without packages."
                : $"Order marked shipped — shipment {shipment.ShipmentNumber} created without packages.";
        }
        catch (Exception ex)
        {
            TempData["ShipError"] = ex.Message;
        }

        return RedirectToPage(new { tab = "ready", ReadySearch, readyPage = ReadyPage > 1 ? ReadyPage : (int?)null });
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ReadyResult = await shipmentService.GetReadyToShipAsync(ReadySearch, ReadyPage, 25, cancellationToken);
        Result = await shipmentService.ListAsync(
            Search, Status, CustomerId, ShipFrom, ShipTo, null, HistoryPage, 25, cancellationToken);
        ViewData["CustomerOptions"] = await db.Customers.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync(cancellationToken);
    }
}
