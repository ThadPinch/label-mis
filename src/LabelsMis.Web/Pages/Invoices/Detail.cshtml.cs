using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Invoices;

[Authorize(Policy = TransactionPolicies.InvoicesRead)]
public class DetailModel(InvoiceService invoiceService) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public PaymentInput Payment { get; set; } = new(DateOnly.FromDateTime(DateTime.UtcNow), 0, PaymentMethod.Check, null, null);
    [BindProperty] public string? VoidReason { get; set; }

    public InvoiceDetail? Detail { get; private set; }
    public bool CanEdit { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        CanEdit = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Accounting);
        Detail = await invoiceService.GetDetailAsync(Id, cancellationToken);
        return Detail is null ? NotFound() : Page();
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
}
