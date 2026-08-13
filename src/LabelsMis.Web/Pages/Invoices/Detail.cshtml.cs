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

    public InvoiceDetail? Detail { get; private set; }
    public bool CanEdit { get; private set; }

    /// <summary>This invoice is void and its order carries no live invoice — a replacement
    /// can be generated at the order's current prices.</summary>
    public bool CanGenerateReplacement { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        CanEdit = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Accounting);
        Detail = await invoiceService.GetDetailAsync(Id, cancellationToken);
        if (Detail is null) return NotFound();

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
            Detail = await invoiceService.GetDetailAsync(Id, cancellationToken);
            CanEdit = true;
            return Page();
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
            Detail = await invoiceService.GetDetailAsync(Id, cancellationToken);
            CanEdit = true;
            return Page();
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
            CanEdit = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Accounting);
            await LoadCanGenerateReplacementAsync(cancellationToken);
            return Page();
        }
    }
}
