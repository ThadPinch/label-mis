using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Dies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LabelsMis.Web.Pages.Dies;

[Authorize(Policy = MasterDataPolicies.Edit)]
public class CreateModel(DieService dieService) : PageModel
{
    [BindProperty]
    public DieFormInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid? duplicateFrom, CancellationToken cancellationToken)
    {
        if (duplicateFrom is Guid sourceId)
        {
            var die = await dieService.GetByIdAsync(sourceId, cancellationToken);
            if (die is null)
            {
                return NotFound();
            }

            Input = DieFormInput.ForDuplicate(die);
        }

        await LoadOptionsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadOptionsAsync(cancellationToken);
            return Page();
        }

        try
        {
            var die = await dieService.CreateAsync(Input.ToForm(), cancellationToken);
            return RedirectToPage("Edit", new { id = die.Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadOptionsAsync(cancellationToken);
            return Page();
        }
    }

    private async Task LoadOptionsAsync(CancellationToken cancellationToken)
    {
        var customers = await dieService.GetCustomerOptionsAsync(cancellationToken);
        ViewData["CustomerOptions"] = customers.Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToList();
        var suppliers = await dieService.GetSupplierOptionsAsync(cancellationToken);
        ViewData["SupplierOptions"] = suppliers.Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToList();
    }
}
