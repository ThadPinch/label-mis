using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Invoices;

[Authorize(Policy = TransactionPolicies.InvoicesEdit)]
public class ExportModel(InvoiceService invoiceService) : PageModel
{
    [BindProperty(SupportsGet = true)] public DateOnly From { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1));
    [BindProperty(SupportsGet = true)] public DateOnly To { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    [BindProperty] public bool ForceReexport { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var (content, fileName, count) = await invoiceService.ExportCsvAsync(From, To, ForceReexport, cancellationToken);
        if (count == 0)
        {
            ModelState.AddModelError(string.Empty, "No invoices matched the export criteria.");
            return Page();
        }

        return File(content, "text/csv", fileName);
    }
}
