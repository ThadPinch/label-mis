using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.Invoices;
using LabelsMis.Web.Services.QuickBooks;
using LabelsMis.Web.Services.SalesOrders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Pages.Invoices;

[Authorize(Policy = TransactionPolicies.InvoicesRead)]
public class DetailModel(
    InvoiceService invoiceService,
    SalesOrderDocumentService documentService,
    QuickBooksSendService quickBooksSend,
    QuickBooksConnectionService quickBooksConnection,
    ClientClock clock,
    LabelsMisDbContext db) : PageModel
{
    /// <summary>How many QuickBooks activity-log rows the invoice's history shows.</summary>
    public const int QuickBooksHistorySize = 20;

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public PaymentInput Payment { get; set; } = new(DateOnly.FromDateTime(DateTime.UtcNow), 0, PaymentMethod.Check, null, null);
    [BindProperty] public string? VoidReason { get; set; }
    [BindProperty] public string? NonBillableReason { get; set; }

    /// <summary>Manual shipping for a draft invoice; blank = automatic (quote / shipments).</summary>
    [BindProperty]
    [System.ComponentModel.DataAnnotations.Range(0, 999999)]
    [System.ComponentModel.DataAnnotations.Display(Name = "Shipping")]
    public decimal? ShippingOverride { get; set; }

    public InvoiceDetail? Detail { get; private set; }
    public bool CanEdit { get; private set; }

    /// <summary>Why this unexported, billable invoice isn't Ready for QuickBooks (empty = Ready).</summary>
    public IReadOnlyList<string> WaitingReasons { get; private set; } = [];

    /// <summary>The invoice is on the Ready / Waiting lists (not exported, non-billable or void).</summary>
    public bool InQuickBooksQueue => Detail is { } d
        && d.Invoice.QbExportedAt is null && !d.Invoice.NonBillable && d.Invoice.Status != InvoiceStatus.Void;

    /// <summary>This invoice is void and its order carries no live invoice — a replacement
    /// can be generated at the order's current prices.</summary>
    public bool CanGenerateReplacement { get; private set; }

    /// <summary>The QuickBooks card: status, numbers, send / retry / cancel, link, email options.</summary>
    public QuickBooksCardModel? QuickBooksCard { get; private set; }

    /// <summary>This invoice's rows in the QuickBooks activity log, newest first (Admin and
    /// Accounting only, like the log page).</summary>
    public IReadOnlyList<QuickBooksLogRow> QuickBooksHistory { get; private set; } = [];

    public string Local(DateTime utc) => clock.ToLocal(utc).ToString("MMM d, yyyy h:mm tt");

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        CanEdit = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Accounting);
        Detail = await invoiceService.GetDetailAsync(Id, cancellationToken);
        if (Detail is null) return NotFound();

        ShippingOverride = Detail.Invoice.ShippingOverride;
        WaitingReasons = await invoiceService.GetWaitingReasonsAsync(Id, cancellationToken);
        await LoadCanGenerateReplacementAsync(cancellationToken);
        await LoadQuickBooksAsync(cancellationToken);
        return Page();
    }

    /// <summary>Send to QuickBooks (or void it there, for a void invoice still live there).</summary>
    public async Task<IActionResult> OnPostQbSendAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Accounting)) return Forbid();
        await QuickBooksUi.QueueOneAsync(quickBooksSend, TempData, Id, cancellationToken);
        return BackToQuickBooksCard();
    }

    public async Task<IActionResult> OnPostQbRetryAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Accounting)) return Forbid();
        await QuickBooksUi.RunAsync(TempData, () => quickBooksSend.RetryAsync(Id, cancellationToken),
            "Back in line for QuickBooks.");
        return BackToQuickBooksCard();
    }

    /// <summary>Takes a queued invoice out of line, or clears Needs attention (settled by hand).</summary>
    public async Task<IActionResult> OnPostQbCancelAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Accounting)) return Forbid();
        await QuickBooksUi.RunAsync(TempData, () => quickBooksSend.CancelAsync(Id, cancellationToken),
            "Nothing is under way for QuickBooks now.");
        return BackToQuickBooksCard();
    }

    /// <summary>"Already in QuickBooks? Link it": finds the QuickBooks invoice with this number.</summary>
    public async Task<IActionResult> OnPostQbLinkAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Accounting)) return Forbid();
        try
        {
            var result = await quickBooksSend.LinkExistingAsync(Id, cancellationToken);
            TempData[result.Linked ? QuickBooksUi.MessageKey : QuickBooksUi.ErrorKey] = result.Message;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            TempData[QuickBooksUi.ErrorKey] = ex.Message;
        }

        return BackToQuickBooksCard();
    }

    /// <summary>Q19: "Don't email this one" and "Email to instead".</summary>
    public async Task<IActionResult> OnPostQbSendOptionsAsync(bool holdSend, string? sendToOverride, CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Accounting)) return Forbid();
        var address = string.IsNullOrWhiteSpace(sendToOverride) ? null : sendToOverride.Trim();
        await QuickBooksUi.RunAsync(TempData,
            () => quickBooksSend.SetSendOptionsAsync(Id, holdSend, address, cancellationToken),
            holdSend
                ? "Saved: QuickBooks will not email this invoice."
                : address is null ? "Saved: QuickBooks emails the customer's billing email." : $"Saved: QuickBooks emails {address}.");
        return BackToQuickBooksCard();
    }

    private RedirectToPageResult BackToQuickBooksCard() =>
        RedirectToPage(pageName: null, pageHandler: null, routeValues: new { id = Id }, fragment: "qb-send-card");

    private async Task LoadQuickBooksAsync(CancellationToken cancellationToken)
    {
        if (Detail is null)
        {
            return;
        }

        var invoice = Detail.Invoice;
        var state = (await quickBooksSend.GetStatesAsync([Id], cancellationToken)).FirstOrDefault();
        var connection = await QuickBooksUi.ReadConnectionAsync(quickBooksConnection, cancellationToken);
        QuickBooksCard = new QuickBooksCardModel(
            Id,
            invoice.InvoiceNumber,
            state,
            connection,
            CanEdit,
            invoice.Status == InvoiceStatus.Void,
            invoice.NonBillable,
            ExportedByHand: invoice.QbExportedAt is not null && invoice.QbInvoiceId is null,
            WaitingReasons,
            invoice.Total,
            invoice.TaxAmount,
            Local);

        if (!CanEdit)
        {
            return;
        }

        var logs = await db.QuickBooksSyncLogs.AsNoTracking()
            .Where(l => l.InvoiceId == Id)
            .OrderByDescending(l => l.CreatedAt)
            .ThenBy(l => l.Id)
            .Take(QuickBooksHistorySize)
            .ToListAsync(cancellationToken);
        var userIds = logs.Select(l => l.CreatedById).Distinct().ToList();
        var users = userIds.Count == 0
            ? new Dictionary<Guid, string?>()
            : await db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Email ?? u.UserName, cancellationToken);
        QuickBooksHistory = logs.Select(l => new QuickBooksLogRow(
            l.CreatedAt, l.Operation, l.Succeeded, l.Automatic, users.GetValueOrDefault(l.CreatedById),
            l.InvoiceId, invoice.InvoiceNumber, l.QuickBooksId, l.HttpStatus, l.ErrorCode, l.Message, l.IntuitTid,
            l.DurationMs)).ToList();
    }

    /// <summary>Flags the invoice Ready before the order ships (deposits, prepay, charge-only
    /// orders), or clears the flag.</summary>
    public async Task<IActionResult> OnPostBillBeforeShipAsync(bool enable, CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Accounting)) return Forbid();
        try
        {
            await invoiceService.SetBillBeforeShipAsync(Id, enable, cancellationToken);
            return RedirectToPage(new { id = Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await ReloadAsync(cancellationToken);
        }
    }

    /// <summary>Sets the manual shipping override (Save) or clears it back to automatic (Clear).</summary>
    public async Task<IActionResult> OnPostShippingAsync(bool clear, CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Accounting)) return Forbid();
        if (!clear && !ModelState.IsValid)
        {
            return await ReloadAsync(cancellationToken);
        }

        try
        {
            await invoiceService.SetShippingOverrideAsync(Id, clear ? null : ShippingOverride, cancellationToken);
            return RedirectToPage(new { id = Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await ReloadAsync(cancellationToken);
        }
    }

    /// <summary>Closes the invoice without a QuickBooks record ($0 samples, internal reprints), or
    /// reopens it (<paramref name="reopen"/>).</summary>
    public async Task<IActionResult> OnPostNonBillableAsync(bool reopen, CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Accounting)) return Forbid();
        try
        {
            await invoiceService.SetNonBillableAsync(Id, !reopen, NonBillableReason, cancellationToken);
            return RedirectToPage(new { id = Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await ReloadAsync(cancellationToken);
        }
    }

    private async Task<IActionResult> ReloadAsync(CancellationToken cancellationToken)
    {
        Detail = await invoiceService.GetDetailAsync(Id, cancellationToken);
        if (Detail is null) return NotFound();
        CanEdit = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Accounting);
        WaitingReasons = await invoiceService.GetWaitingReasonsAsync(Id, cancellationToken);
        await LoadCanGenerateReplacementAsync(cancellationToken);
        await LoadQuickBooksAsync(cancellationToken);
        return Page();
    }

    private async Task LoadCanGenerateReplacementAsync(CancellationToken cancellationToken)
    {
        if (Detail is null || Detail.Invoice.Status != InvoiceStatus.Void || !CanEdit)
        {
            CanGenerateReplacement = false;
            return;
        }

        CanGenerateReplacement = await db.SalesOrders.AsNoTracking()
                .AnyAsync(o => o.Id == Detail.SalesOrderId && o.Status != SalesOrderStatus.Cancelled, cancellationToken)
            && !await db.Invoices.AsNoTracking()
                .AnyAsync(i => i.SalesOrderId == Detail.SalesOrderId && i.Status != InvoiceStatus.Void, cancellationToken);
    }

    public async Task<IActionResult> OnGetPdfAsync(CancellationToken cancellationToken)
    {
        var pdf = await invoiceService.RenderPdfAsync(Id, cancellationToken);
        return pdf is null
            ? NotFound()
            : File(pdf.Bytes, "application/pdf", $"{pdf.InvoiceNumber.Replace('/', '-')}.pdf");
    }

    public async Task<IActionResult> OnGetDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var detail = await invoiceService.GetDetailAsync(Id, cancellationToken);
        if (detail is null) return NotFound();

        var file = await documentService.OpenAsync(detail.SalesOrderId, documentId, cancellationToken);
        if (file is null) return NotFound();

        return File(file.Value.Stream, file.Value.ContentType, file.Value.FileName);
    }

    public async Task<IActionResult> OnPostRecordPaymentAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Accounting)) return Forbid();
        try
        {
            await invoiceService.RecordPaymentAsync(Id, Payment, cancellationToken);
            return RedirectToPage(new { id = Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await ReloadAsync(cancellationToken);
        }
    }

    public async Task<IActionResult> OnPostVoidAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin)) return Forbid();
        try
        {
            await invoiceService.VoidAsync(Id, VoidReason ?? string.Empty, cancellationToken);
            return RedirectToPage(new { id = Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await ReloadAsync(cancellationToken);
        }
    }

    /// <summary>Generates a replacement draft invoice for this void invoice's order at the
    /// order's current prices, then lands on the new invoice. The service refuses while a
    /// non-void invoice exists for the order.</summary>
    public async Task<IActionResult> OnPostGenerateReplacementAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Accounting)) return Forbid();

        Detail = await invoiceService.GetDetailAsync(Id, cancellationToken);
        if (Detail is null) return NotFound();

        try
        {
            var invoice = await invoiceService.CreateFromSalesOrderAsync(Detail.SalesOrderId, cancellationToken);
            return RedirectToPage(new { id = invoice.Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await ReloadAsync(cancellationToken);
        }
    }
}
