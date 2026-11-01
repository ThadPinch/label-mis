using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Invoices;
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
    LabelsMisDbContext db) : PageModel
{
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

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        CanEdit = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Accounting);
        Detail = await invoiceService.GetDetailAsync(Id, cancellationToken);
        if (Detail is null) return NotFound();

        ShippingOverride = Detail.Invoice.ShippingOverride;
        WaitingReasons = await invoiceService.GetWaitingReasonsAsync(Id, cancellationToken);
        await LoadCanGenerateReplacementAsync(cancellationToken);
        return Page();
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
