using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Invoices;
using LabelsMis.Web.Services.Shipments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Shipments;

[Authorize(Policy = TransactionPolicies.ShippingRead)]
public class DetailModel(ShipmentService shipmentService, InvoiceService invoiceService) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

    public ShipmentDetail? Detail { get; private set; }
    public bool CanInvoice { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Detail = await shipmentService.GetDetailAsync(Id, cancellationToken);
        if (Detail is null) return NotFound();

        CanInvoice = (User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Accounting))
            && Detail.Shipment.Status is ShipmentStatus.InTransit or ShipmentStatus.Delivered;
        return Page();
    }

    public async Task<IActionResult> OnPostGenerateInvoiceAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Accounting)) return Forbid();
        var invoice = await invoiceService.CreateFromShipmentAsync(Id, cancellationToken);
        return RedirectToPage("/Invoices/Detail", new { id = invoice.Id });
    }
}
