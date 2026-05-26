using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Presses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Presses;

[Authorize(Policy = MasterDataPolicies.Edit)]
public class CreateModel(PressService pressService) : PageModel
{
    [BindProperty]
    public PressFormInput Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var press = await pressService.CreateAsync(Input.ToForm(), cancellationToken);
            return RedirectToPage("Edit", new { id = press.Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }
}
