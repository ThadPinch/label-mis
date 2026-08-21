using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Customers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Estimates;

/// <summary>JSON: a customer's standing notes, fetched by the estimate form when the customer is picked
/// so they can pre-fill the header notes.</summary>
[Authorize(Policy = TransactionPolicies.EstimatesEdit)]
public class CustomerNotesModel(CustomerService customerService) : PageModel
{
    public async Task<IActionResult> OnGetAsync(Guid? customerId, CancellationToken cancellationToken)
    {
        if (customerId is not { } id || id == Guid.Empty)
        {
            return new JsonResult(new { notes = (string?)null });
        }

        return new JsonResult(new { notes = await customerService.GetNotesAsync(id, cancellationToken) });
    }
}
