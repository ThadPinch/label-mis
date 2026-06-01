using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.FinishingOperations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.FinishingOperations;

[Authorize(Policy = MasterDataPolicies.Edit)]
public class CreateModel(FinishingOperationService finishingOperationService) : PageModel
{
    [BindProperty]
    public FinishingOperationFormInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid? duplicateFrom, CancellationToken cancellationToken)
    {
        if (duplicateFrom is Guid sourceId)
        {
            var operation = await finishingOperationService.GetByIdAsync(sourceId, cancellationToken);
            if (operation is null)
            {
                return NotFound();
            }

            Input = FinishingOperationFormInput.ForDuplicate(operation);
        }

        await LoadLookupsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadLookupsAsync(cancellationToken);
            return Page();
        }

        try
        {
            var operation = await finishingOperationService.CreateAsync(Input.ToForm(), cancellationToken);
            return RedirectToPage("Edit", new { id = operation.Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadLookupsAsync(cancellationToken);
            return Page();
        }
    }

    private async Task LoadLookupsAsync(CancellationToken cancellationToken) =>
        ViewData["Dies"] = await finishingOperationService.GetDieSelectListAsync(cancellationToken);
}
