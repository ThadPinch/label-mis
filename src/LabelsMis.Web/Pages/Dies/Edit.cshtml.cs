using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.Dies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LabelsMis.Web.Pages.Dies;

[Authorize(Policy = MasterDataPolicies.Read)]
public class EditModel(DieService dieService, ICurrentUserService currentUser) : PageModel
{
    [BindProperty]
    public DieFormInput Input { get; set; } = new();

    public bool CanEdit => currentUser.CanEditMasterData;
    public bool IsActive { get; private set; } = true;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var die = await dieService.GetByIdAsync(id, cancellationToken);
        if (die is null)
        {
            return NotFound();
        }

        Input = DieFormInput.FromEntity(die);
        IsActive = die.IsActive;
        await LoadOptionsAsync(cancellationToken);
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
            await dieService.UpdateAsync(id, Input.ToForm(), cancellationToken);
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

        await dieService.DeactivateAsync(id, cancellationToken);
        return RedirectToPage("Index");
    }

    private async Task ReloadAsync(Guid id, CancellationToken cancellationToken)
    {
        var die = await dieService.GetByIdAsync(id, cancellationToken);
        IsActive = die?.IsActive ?? false;
        await LoadOptionsAsync(cancellationToken);
    }

    private async Task LoadOptionsAsync(CancellationToken cancellationToken)
    {
        var customers = await dieService.GetCustomerOptionsAsync(cancellationToken);
        ViewData["CustomerOptions"] = customers.Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToList();
        var suppliers = await dieService.GetSupplierOptionsAsync(cancellationToken);
        ViewData["SupplierOptions"] = suppliers.Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToList();
    }
}
