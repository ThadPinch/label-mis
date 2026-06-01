using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Shipping;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.ShippingMethods;

[Authorize(Policy = MasterDataPolicies.Edit)]
public class CreateModel(ShippingMethodService shippingMethodService) : PageModel
{
    [BindProperty]
    public ShippingMethodFormInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid? duplicateFrom, CancellationToken cancellationToken)
    {
        if (duplicateFrom is Guid sourceId)
        {
            var method = await shippingMethodService.GetByIdAsync(sourceId, cancellationToken);
            if (method is null)
            {
                return NotFound();
            }

            Input = ShippingMethodFormInput.ForDuplicate(method);
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
            var method = await shippingMethodService.CreateAsync(Input.ToForm(), cancellationToken);
            return RedirectToPage("Edit", new { id = method.Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }
}
