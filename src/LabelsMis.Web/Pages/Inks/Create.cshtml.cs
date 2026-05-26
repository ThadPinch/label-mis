using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Inks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Inks;

[Authorize(Policy = MasterDataPolicies.Edit)]
public class CreateModel(InkService inkService) : PageModel
{
    [BindProperty]
    public InkFormInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid? duplicateFrom, CancellationToken cancellationToken)
    {
        if (duplicateFrom is Guid sourceId)
        {
            var ink = await inkService.GetByIdAsync(sourceId, cancellationToken);
            if (ink is null)
            {
                return NotFound();
            }

            Input = InkFormInput.ForDuplicate(ink);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var ink = await inkService.CreateAsync(Input.ToForm(), cancellationToken);
            return RedirectToPage("Edit", new { id = ink.Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }
}
