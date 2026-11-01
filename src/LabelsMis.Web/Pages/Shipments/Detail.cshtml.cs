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

    /// <summary>The carrier's actual charge (WWEX); blank clears it.</summary>
    [BindProperty]
    [System.ComponentModel.DataAnnotations.Range(0, 999999)]
    public decimal? CarrierCost { get; set; }

    public ShipmentDetail? Detail { get; private set; }
    public bool CanInvoice { get; private set; }

    /// <summary>Shipping staff record the carrier cost; accounting enters it from the WWEX bill.</summary>
    public bool CanEditCarrierCost =>
        User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Shipping) || User.IsInRole(AppRoles.Accounting);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await LoadAsync(cancellationToken)) return NotFound();
        CarrierCost = Detail!.Shipment.CarrierCost;
        return Page();
    }

    public async Task<IActionResult> OnPostCarrierCostAsync(CancellationToken cancellationToken)
    {
        if (!CanEditCarrierCost) return Forbid();
        if (!ModelState.IsValid)
        {
            return await LoadAsync(cancellationToken) ? Page() : NotFound();
        }

        try
        {
            await shipmentService.SetCarrierCostAsync(Id, CarrierCost, cancellationToken);
            TempData["ShipmentStatus"] = "Carrier cost saved.";
            return RedirectToPage(new { id = Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await LoadAsync(cancellationToken) ? Page() : NotFound();
        }
    }

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        Detail = await shipmentService.GetDetailAsync(Id, cancellationToken);
        if (Detail is null) return false;

        CanInvoice = (User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Accounting))
            && Detail.Shipment.Status is ShipmentStatus.InTransit or ShipmentStatus.Delivered;
        return true;
    }

    public async Task<IActionResult> OnPostGenerateInvoiceAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Accounting)) return Forbid();
        var invoice = await invoiceService.CreateFromShipmentAsync(Id, cancellationToken);
        return RedirectToPage("/Invoices/Detail", new { id = invoice.Id });
    }
}
