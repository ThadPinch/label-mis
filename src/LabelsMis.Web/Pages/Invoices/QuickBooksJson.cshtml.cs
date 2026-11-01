using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.QuickBooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Invoices;

/// <summary>The invoice as QuickBooks will receive it (E9), as JSON. Same authorization as the
/// invoice pages; 404 for an unknown id.</summary>
[Authorize(Policy = TransactionPolicies.InvoicesRead)]
public class QuickBooksJsonModel(QuickBooksInvoiceService quickBooksInvoices) : PageModel
{
    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var preview = await quickBooksInvoices.GetPreviewAsync(id, cancellationToken);
        return preview is null ? NotFound() : new JsonResult(preview);
    }
}
