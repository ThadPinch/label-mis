using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Suppliers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Suppliers;

[Authorize(Policy = MasterDataPolicies.Edit)]
public class CreateModel(SupplierService supplierService) : PageModel
{
    [BindProperty]
    public SupplierFormInput Input { get; set; } = new();

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
            var supplier = await supplierService.CreateAsync(Input.ToForm(), cancellationToken);
            return RedirectToPage("Edit", new { id = supplier.Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }
}
