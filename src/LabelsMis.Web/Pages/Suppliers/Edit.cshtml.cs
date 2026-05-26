using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.Suppliers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Suppliers;

[Authorize(Policy = MasterDataPolicies.Read)]
public class EditModel(SupplierService supplierService, ICurrentUserService currentUser) : PageModel
{
    [BindProperty]
    public SupplierFormInput Input { get; set; } = new();

    public bool CanEdit => currentUser.CanEditMasterData;
    public bool IsActive { get; private set; } = true;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var supplier = await supplierService.GetByIdAsync(id, cancellationToken);
        if (supplier is null)
        {
            return NotFound();
        }

        Input = SupplierFormInput.FromEntity(supplier);
        IsActive = supplier.IsActive;
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
            await supplierService.UpdateAsync(id, Input.ToForm(), cancellationToken);
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

        await supplierService.DeactivateAsync(id, cancellationToken);
        return RedirectToPage("Index");
    }

    private async Task ReloadAsync(Guid id, CancellationToken cancellationToken)
    {
        var supplier = await supplierService.GetByIdAsync(id, cancellationToken);
        IsActive = supplier?.IsActive ?? false;
    }
}
