using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Stocks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LabelsMis.Web.Pages.Stocks;

[Authorize(Policy = MasterDataPolicies.Edit)]
public class CreateModel(StockService stockService) : PageModel
{
    [BindProperty]
    public StockFormInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid? duplicateFrom, CancellationToken cancellationToken)
    {
        if (duplicateFrom is Guid sourceId)
        {
            var stock = await stockService.GetByIdAsync(sourceId, cancellationToken);
            if (stock is null)
            {
                return NotFound();
            }

            Input = StockFormInput.ForDuplicate(stock);
        }

        await LoadSupplierOptionsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadSupplierOptionsAsync(cancellationToken);
            return Page();
        }

        try
        {
            var stock = await stockService.CreateAsync(Input.ToForm(), cancellationToken);
            return RedirectToPage("Edit", new { id = stock.Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadSupplierOptionsAsync(cancellationToken);
            return Page();
        }
    }

    private async Task LoadSupplierOptionsAsync(CancellationToken cancellationToken)
    {
        var suppliers = await stockService.GetSupplierOptionsAsync(cancellationToken);
        ViewData["SupplierOptions"] = suppliers.Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToList();
    }
}
