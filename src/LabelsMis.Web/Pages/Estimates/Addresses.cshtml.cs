using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Customers;
using LabelsMis.Web.Services.Shipping;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Estimates;

[Authorize(Policy = TransactionPolicies.EstimatesEdit)]
public class AddressesModel(CustomerService customerService) : PageModel
{
    public Task<IActionResult> OnGetAsync(Guid? customerId, CancellationToken cancellationToken) =>
        ShipToAddressJson.BuildAsync(customerService, customerId, cancellationToken);
}
