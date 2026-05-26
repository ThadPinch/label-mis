using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.PurchaseOrders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.PurchaseOrders;

[Authorize(Policy = TransactionPolicies.InventoryEdit)]
public class ReceiveModel(PurchaseOrderService purchaseOrderService) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public List<ReceiveLineInput> Lines { get; set; } = [];

    public Domain.Entities.PurchaseOrder? Po { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Po = await purchaseOrderService.GetAsync(Id, cancellationToken);
        if (Po is null) return NotFound();

        Lines = Po.Lines.OrderBy(l => l.LineNumber).Select(l => new ReceiveLineInput(
            l.Id, 0, string.Empty, 1, null)).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        try
        {
            await purchaseOrderService.ReceiveAsync(Id, new ReceiveFormInput(Lines), cancellationToken);
            return RedirectToPage("Edit", new { id = Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            Po = await purchaseOrderService.GetAsync(Id, cancellationToken);
            return Page();
        }
    }
}
