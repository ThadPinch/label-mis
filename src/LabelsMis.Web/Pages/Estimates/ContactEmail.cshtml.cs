using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Customers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Estimates;

[Authorize(Policy = TransactionPolicies.EstimatesEdit)]
public class ContactEmailModel(CustomerService customerService) : PageModel
{
    public async Task<IActionResult> OnGetAsync(Guid? customerId, CancellationToken cancellationToken)
    {
        if (customerId is not { } id || id == Guid.Empty)
        {
            return new JsonResult(new { email = (string?)null, hasContacts = false, contacts = Array.Empty<object>() });
        }

        var customer = await customerService.GetByIdAsync(id, cancellationToken);
        var contacts = customer?.Contacts ?? [];

        // Primary contact first, then any other contact — pick the first one with an email.
        var withEmail = contacts
            .Where(c => !string.IsNullOrWhiteSpace(c.Email))
            .OrderByDescending(c => c.IsPrimary)
            .ToList();
        var email = withEmail.Select(c => c.Email).FirstOrDefault();

        return new JsonResult(new
        {
            email,
            hasContacts = contacts.Count > 0,
            contacts = withEmail.Select(c => new { name = c.FullName, email = c.Email })
        });
    }
}
