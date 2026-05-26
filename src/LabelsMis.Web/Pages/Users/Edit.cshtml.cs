using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Users;

[Authorize(Policy = TransactionPolicies.AdminOverride)]
public class EditModel(UserAdminService userAdminService) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public UserPageInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var detail = await userAdminService.GetAsync(Id, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        Input = UserPageInput.FromDetail(detail);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Input.IsEdit = true;

        if (Input.SelectedRoles.Count == 0)
        {
            ModelState.AddModelError("Input.SelectedRoles", "Select at least one role.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            await userAdminService.UpdateAsync(
                Id,
                new UpdateUserInput(Input.SelectedRoles, Input.IsLockedOut, Input.MustChangePassword, Input.NewPassword),
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
