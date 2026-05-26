using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.PurchaseOrders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Pages.PurchaseOrders;

[Authorize(Policy = TransactionPolicies.InventoryEdit)]
public class CreateModel(PurchaseOrderService purchaseOrderService, LabelsMisDbContext db) : PageModel
{
    [BindProperty] public PurchaseOrderFormInput Input { get; set; } = new(Guid.Empty, null, null, [new(null, Guid.Empty, 0, 0)]);

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        await LoadLookupsAsync(cancellationToken);

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadLookupsAsync(cancellationToken);
            return Page();
        }

        try
        {
            var po = await purchaseOrderService.CreateAsync(Input, cancellationToken);
            return RedirectToPage("Edit", new { id = po.Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadLookupsAsync(cancellationToken);
            return Page();
        }
    }

    private async Task LoadLookupsAsync(CancellationToken cancellationToken)
    {
        ViewData["Suppliers"] = await db.Suppliers.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Name)
            .Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToListAsync(cancellationToken);
        ViewData["Stocks"] = await db.Stocks.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Code)
            .Select(s => new SelectListItem($"{s.Code} — {s.Description}", s.Id.ToString())).ToListAsync(cancellationToken);
    }
}
