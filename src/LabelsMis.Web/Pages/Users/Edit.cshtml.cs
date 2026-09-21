using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Users;

[Authorize(Policy = TransactionPolicies.AdminOverride)]
public class EditModel(UserAdminService userAdminService) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public UserPageInput Input { get; set; } = new();

    /// <summary>The email as currently stored, for the heading; Input.Email may hold a rejected edit.</summary>
    public string CurrentEmail { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var detail = await userAdminService.GetAsync(Id, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        Input = UserPageInput.FromDetail(detail);
        CurrentEmail = detail.Email;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Input.IsEdit = true;

        var detail = await userAdminService.GetAsync(Id, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        CurrentEmail = detail.Email;

        if (Input.SelectedRoles.Count == 0)
        {
            ModelState.AddModelError("Input.SelectedRoles", "Select at least one role.");
        }

        if (ModelState.GetValidationState("Input.Email") == ModelValidationState.Valid
            && await userAdminService.IsEmailTakenAsync(Input.Email, Id, cancellationToken))
        {
            ModelState.AddModelError("Input.Email", "Another user already has this email.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            await userAdminService.UpdateAsync(
                Id,
                new UpdateUserInput(Input.Email, Input.SelectedRoles, Input.IsLockedOut, Input.MustChangePassword, Input.NewPassword),
                cancellationToken);
            return RedirectToPage(new { id = Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }
}
