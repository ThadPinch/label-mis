using LabelsMis.Web.Authorization;
using LabelsMis.Web.Pages.Shared;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.Stocks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LabelsMis.Web.Pages.Stocks;

[Authorize(Policy = MasterDataPolicies.Read)]
public class EditModel(StockService stockService, ICurrentUserService currentUser) : PageModel
{
    [BindProperty]
    public StockFormInput Input { get; set; } = new();

    public bool CanEdit => currentUser.CanEditMasterData;
    public bool IsActive { get; private set; } = true;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var stock = await stockService.GetByIdAsync(id, cancellationToken);
        if (stock is null)
        {
            return NotFound();
        }

        Input = StockFormInput.FromEntity(stock);
        IsActive = stock.IsActive;
        await LoadSupplierOptionsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!CanEdit)
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            await ReloadAsync(id, cancellationToken);
            return Page();
        }

        try
        {
            await stockService.UpdateAsync(id, Input.ToForm(), cancellationToken);
            return RedirectToPage(new { id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await ReloadAsync(id, cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!CanEdit)
        {
            return Forbid();
        }

        await stockService.DeactivateAsync(id, cancellationToken);
        return this.RedirectToListPage();
    }

    private async Task ReloadAsync(Guid id, CancellationToken cancellationToken)
    {
        var stock = await stockService.GetByIdAsync(id, cancellationToken);
        IsActive = stock?.IsActive ?? false;
        await LoadSupplierOptionsAsync(cancellationToken);
    }

    private async Task LoadSupplierOptionsAsync(CancellationToken cancellationToken)
    {
        var suppliers = await stockService.GetSupplierOptionsAsync(cancellationToken);
        ViewData["SupplierOptions"] = suppliers.Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToList();
    }
}
