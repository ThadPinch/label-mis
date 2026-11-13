using LabelsMis.Infrastructure.Identity;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.QuickBooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Invoices;

/// <summary>"Preview for QuickBooks" (E10): the invoice laid out like the QuickBooks invoice
/// Sabrina keys, blockers in red at the top, per-line item / class dropdowns that save at once.
/// With QuickBooks connected it also sends the invoice (the compact QuickBooks card).</summary>
[Authorize(Policy = TransactionPolicies.InvoicesRead)]
public class QuickBooksModel(
    QuickBooksInvoiceService quickBooksInvoices,
    Services.Invoices.InvoiceService invoiceService,
    QuickBooksSendService quickBooksSend,
    QuickBooksConnectionService quickBooksConnection,
    ClientClock clock) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

    public QbInvoicePreview? Preview { get; private set; }
    public bool CanEdit => User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Accounting);
    public bool CanEditOverrides => CanEdit && Preview?.CanEditOverrides == true;

    /// <summary>Status and Send / Retry / Cancel for this invoice (compact: no link or email options).</summary>
    public QuickBooksCardModel? QuickBooksCard { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Preview = await quickBooksInvoices.GetPreviewAsync(Id, cancellationToken);
        if (Preview is null) return NotFound();
        await LoadQuickBooksCardAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostQbSendAsync(CancellationToken cancellationToken)
    {
        if (!CanEdit) return Forbid();
        await QuickBooksUi.QueueOneAsync(quickBooksSend, TempData, Id, cancellationToken);
        return BackToQuickBooksCard();
    }

    public async Task<IActionResult> OnPostQbRetryAsync(CancellationToken cancellationToken)
    {
        if (!CanEdit) return Forbid();
        await QuickBooksUi.RunAsync(TempData, () => quickBooksSend.RetryAsync(Id, cancellationToken),
            "Back in line for QuickBooks.");
        return BackToQuickBooksCard();
    }

    public async Task<IActionResult> OnPostQbCancelAsync(CancellationToken cancellationToken)
    {
        if (!CanEdit) return Forbid();
        await QuickBooksUi.RunAsync(TempData, () => quickBooksSend.CancelAsync(Id, cancellationToken),
            "Nothing is under way for QuickBooks now.");
        return BackToQuickBooksCard();
    }

    private RedirectToPageResult BackToQuickBooksCard() =>
        RedirectToPage(pageName: null, pageHandler: null, routeValues: new { id = Id }, fragment: "qb-send-card");

    private async Task<IActionResult> ReloadAsync(CancellationToken cancellationToken)
    {
        Preview = await quickBooksInvoices.GetPreviewAsync(Id, cancellationToken);
        if (Preview is null) return NotFound();
        await LoadQuickBooksCardAsync(cancellationToken);
        return Page();
    }

    private async Task LoadQuickBooksCardAsync(CancellationToken cancellationToken)
    {
        if (Preview is not { } p)
        {
            return;
        }

        var state = (await quickBooksSend.GetStatesAsync([Id], cancellationToken)).FirstOrDefault();
        var connection = await QuickBooksUi.ReadConnectionAsync(quickBooksConnection, cancellationToken);
        QuickBooksCard = new QuickBooksCardModel(
            Id,
            p.DocNumber,
            state,
            connection,
            CanEdit,
            IsVoid: p.Status == QbInvoiceStatus.Void,
            NonBillable: p.Status == QbInvoiceStatus.NonBillable,
            ExportedByHand: p.ExportedAt is not null && state?.QuickBooksInvoiceId is null,
            WaitingReasons: p.Blockers,
            LabelShopTotal: p.LabelShopTotal,
            LabelShopTax: p.LabelShopTax,
            Local: utc => clock.ToLocal(utc).ToString("MMM d, yyyy h:mm tt"),
            Compact: true);
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
            return await ReloadAsync(cancellationToken);
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
            return await ReloadAsync(cancellationToken);
        }
    }
}
