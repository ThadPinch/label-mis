using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.Shipping;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.ShippingMethods;

[Authorize(Policy = MasterDataPolicies.Read)]
public class EditModel(ShippingMethodService shippingMethodService, ICurrentUserService currentUser) : PageModel
{
    [BindProperty]
    public ShippingMethodFormInput Input { get; set; } = new();

    public bool CanEdit => currentUser.CanEditMasterData;
    public bool IsActive { get; private set; } = true;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var method = await shippingMethodService.GetByIdAsync(id, cancellationToken);
        if (method is null)
        {
            return NotFound();
        }

        Input = ShippingMethodFormInput.FromEntity(method);
        IsActive = method.IsActive;
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
            await shippingMethodService.UpdateAsync(id, Input.ToForm(), cancellationToken);
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

        await shippingMethodService.DeactivateAsync(id, cancellationToken);
        return RedirectToPage("Index");
    }

    private async Task ReloadAsync(Guid id, CancellationToken cancellationToken)
    {
        var method = await shippingMethodService.GetByIdAsync(id, cancellationToken);
        IsActive = method?.IsActive ?? false;
    }
}
