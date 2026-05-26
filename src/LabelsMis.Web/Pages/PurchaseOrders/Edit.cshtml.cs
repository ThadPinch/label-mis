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
public class EditModel(PurchaseOrderService purchaseOrderService, LabelsMisDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public PurchaseOrderFormInput Input { get; set; } = new(Guid.Empty, null, null, []);

    public Domain.Entities.PurchaseOrder? Po { get; private set; }
    public bool CanEdit { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Po = await purchaseOrderService.GetAsync(Id, cancellationToken);
        if (Po is null) return NotFound();

        CanEdit = Po.Status == PurchaseOrderStatus.Draft;
        Input = new PurchaseOrderFormInput(
            Po.SupplierId,
            Po.ExpectedAt,
            Po.Notes,
            Po.Lines.OrderBy(l => l.LineNumber).Select(l => new PurchaseOrderLineInput(l.Id, l.StockId, l.QuantityLf, l.UnitCost)).ToList());

        await LoadLookupsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Scheduler")) return Forbid();
        await purchaseOrderService.UpdateAsync(Id, Input, cancellationToken);
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostSendAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Scheduler")) return Forbid();
        await purchaseOrderService.SendAsync(Id, cancellationToken);
        return RedirectToPage(new { id = Id });
    }

    private async Task LoadLookupsAsync(CancellationToken cancellationToken)
    {
        ViewData["Suppliers"] = await db.Suppliers.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Name)
            .Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToListAsync(cancellationToken);
        ViewData["Stocks"] = await db.Stocks.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Code)
            .Select(s => new SelectListItem($"{s.Code} — {s.Description}", s.Id.ToString())).ToListAsync(cancellationToken);
    }
}
