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
    [BindProperty] public bool SendEmail { get; set; } = true;
    [BindProperty] public string? EmailTo { get; set; }

    public Domain.Entities.PurchaseOrder? Po { get; private set; }
    public bool CanEdit { get; private set; }
    public IReadOnlyList<SelectListItem> ContactEmails { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Po = await purchaseOrderService.GetAsync(Id, cancellationToken);
        if (Po is null) return NotFound();

        CanEdit = Po.Status == PurchaseOrderStatus.Draft;
        Input = new PurchaseOrderFormInput(
            Po.SupplierId,
            Po.ExpectedAt,
            Po.Notes,
            Po.Lines.OrderBy(l => l.LineNumber).Select(l => new PurchaseOrderLineInput(l.Id, l.StockId, l.QuantityLf)).ToList());

        LoadContactEmails(Po);
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
        await purchaseOrderService.SendAsync(Id, SendEmail, EmailTo, cancellationToken);
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Scheduler")) return Forbid();
        await purchaseOrderService.DeleteDraftAsync(Id, cancellationToken);
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnGetPdfAsync(CancellationToken cancellationToken)
    {
        var bytes = await purchaseOrderService.RenderPdfBytesAsync(Id, cancellationToken);
        return bytes is null ? NotFound() : File(bytes, "application/pdf");
    }

    private void LoadContactEmails(Domain.Entities.PurchaseOrder po)
    {
        ContactEmails = po.Supplier.Contacts
            .Where(c => !string.IsNullOrWhiteSpace(c.Email))
            .OrderByDescending(c => c.IsPrimary)
            .Select(c => new SelectListItem($"{c.FullName} — {c.Email}", c.Email))
            .ToList();

        EmailTo = po.Supplier.Contacts
            .OrderByDescending(c => c.IsPrimary)
            .Select(c => c.Email)
            .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));
    }

    private async Task LoadLookupsAsync(CancellationToken cancellationToken)
    {
        ViewData["StockOptions"] = await db.Stocks.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Code)
            .Select(s => new StockOptionView(s.Id, $"{s.Code} — {s.Description}", s.StockType.ToString(), s.MinOrderQtyLf, s.SupplierId, s.CostPerMsi, s.WidthIn))
            .ToListAsync(cancellationToken);
    }
}
