using LabelsMis.Web.Authorization;
using LabelsMis.Web.Pages.Shared;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.Presses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Presses;

[Authorize(Policy = MasterDataPolicies.Read)]
public class EditModel(PressService pressService, ICurrentUserService currentUser) : PageModel
{
    [BindProperty]
    public PressFormInput Input { get; set; } = new();

    public bool CanEdit => currentUser.CanEditMasterData;
    public bool IsActive { get; private set; } = true;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var press = await pressService.GetByIdAsync(id, cancellationToken);
        if (press is null)
        {
            return NotFound();
        }

        Input = PressFormInput.FromEntity(press);
        IsActive = press.IsActive;
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
            await pressService.UpdateAsync(id, Input.ToForm(), cancellationToken);
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

        await pressService.DeactivateAsync(id, cancellationToken);
        return this.RedirectToListPage();
    }

    private async Task ReloadAsync(Guid id, CancellationToken cancellationToken)
    {
        var press = await pressService.GetByIdAsync(id, cancellationToken);
        IsActive = press?.IsActive ?? false;
    }
}
