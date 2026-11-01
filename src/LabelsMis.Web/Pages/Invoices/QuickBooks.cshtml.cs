using LabelsMis.Infrastructure.Identity;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.QuickBooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Invoices;

/// <summary>"Preview for QuickBooks" (E10): the invoice laid out like the QuickBooks invoice
/// Sabrina keys, blockers in red at the top, per-line item / class dropdowns that save at once.</summary>
[Authorize(Policy = TransactionPolicies.InvoicesRead)]
public class QuickBooksModel(QuickBooksInvoiceService quickBooksInvoices, Services.Invoices.InvoiceService invoiceService) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

    public QbInvoicePreview? Preview { get; private set; }
    public bool CanEdit => User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Accounting);
    public bool CanEditOverrides => CanEdit && Preview?.CanEditOverrides == true;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Preview = await quickBooksInvoices.GetPreviewAsync(Id, cancellationToken);
        return Preview is null ? NotFound() : Page();
    }

    /// <summary>E11: the invoice is in QuickBooks — mark it exported (who and when are recorded)
    /// and go back to the Ready list for the next one. A Waiting invoice needs "export anyway".</summary>
    public async Task<IActionResult> OnPostMarkExportedAsync(bool exportAnyway, CancellationToken cancellationToken)
    {
        if (!CanEdit) return Forbid();
        try
        {
            var invoiceNumber = await invoiceService.MarkOneExportedAsync(Id, exportAnyway, cancellationToken);
            TempData["ExportMessage"] = $"{invoiceNumber} marked as exported to QuickBooks.";
            return RedirectToPage("Index", new { Tab = IndexModel.ReadyTab });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            Preview = await quickBooksInvoices.GetPreviewAsync(Id, cancellationToken);
            return Preview is null ? NotFound() : Page();
        }
    }

    /// <summary>Saves one line's item / class override (blank = back to the mapping). A fetch
    /// (X-Requested-With: fetch) gets JSON with the line's new mapping; a plain post reloads.</summary>
    public async Task<IActionResult> OnPostOverrideAsync(
        Guid lineId,
        string? item,
        string? qbClass,
        CancellationToken cancellationToken)
    {
        if (!CanEdit) return Forbid();
        var isFetch = string.Equals(Request.Headers["X-Requested-With"], "fetch", StringComparison.OrdinalIgnoreCase);
        try
        {
            var saved = await quickBooksInvoices.SetLineOverridesAsync(Id, lineId, item, qbClass, cancellationToken);
            if (isFetch)
            {
                return new JsonResult(new
                {
                    lineId,
                    item = saved.Mapping.Item,
                    @class = saved.Mapping.Class,
                    itemSource = saved.Mapping.ItemSource,
                    classSource = saved.Mapping.ClassSource
                });
            }

            TempData["QuickBooksPreviewStatus"] = $"Line {saved.LineNumber} saved.";
            return RedirectToPage(new { id = Id });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            if (isFetch)
            {
                return new JsonResult(new { error = ex.Message }) { StatusCode = StatusCodes.Status400BadRequest };
            }

            ModelState.AddModelError(string.Empty, ex.Message);
            Preview = await quickBooksInvoices.GetPreviewAsync(Id, cancellationToken);
            return Preview is null ? NotFound() : Page();
        }
    }
}
