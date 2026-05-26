using System.ComponentModel.DataAnnotations;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Users;

[Authorize(Policy = TransactionPolicies.AdminOverride)]
public class CreateModel(UserAdminService userAdminService) : PageModel
{
    [BindProperty]
    public UserPageInput Input { get; set; } = UserPageInput.ForCreate();

    public void OnGet() => Input = UserPageInput.ForCreate();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Input.Password))
        {
            ModelState.AddModelError("Input.Password", "Password is required.");
        }

        if (Input.SelectedRoles.Count == 0)
        {
            ModelState.AddModelError("Input.SelectedRoles", "Select at least one role.");
        }

        if (!ModelState.IsValid)
        {
            Input.IsEdit = false;
            return Page();
        }

        try
        {
            var user = await userAdminService.CreateAsync(
                new CreateUserInput(Input.Email, Input.Password!, Input.SelectedRoles, Input.MustChangePassword),
                cancellationToken);
            return RedirectToPage("Edit", new { id = user.Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            Input.IsEdit = false;
            return Page();
        }
    }
}
