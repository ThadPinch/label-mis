using LabelsMis.Domain.Carriers;
using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.Carriers;
using LabelsMis.Web.Services.Invoices;
using LabelsMis.Web.Services.Shipments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Shipments;

[Authorize(Policy = TransactionPolicies.ShippingRead)]
public class DetailModel(
    ShipmentService shipmentService,
    InvoiceService invoiceService,
    CarrierShipmentService carrierShipments,
    CarrierSettingsService carrierSettings,
    ClientClock clock) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

    /// <summary>The carrier's actual charge (WWEX); blank clears it.</summary>
    [BindProperty]
    [System.ComponentModel.DataAnnotations.Range(0, 999999)]
    [System.ComponentModel.DataAnnotations.Display(Name = "Carrier cost")]
    public decimal? CarrierCost { get; set; }

    public ShipmentDetail? Detail { get; private set; }
    public ShipmentBillingContext? Billing { get; private set; }
    public bool CanInvoice { get; private set; }

    /// <summary>The order's per-line shipped quantities (for Mark order shipped after an under-run).</summary>
    public OrderShipProgress? OrderProgress { get; private set; }

    /// <summary>The label file the shop prints from (Setup → Shipping carrier); its button comes first.</summary>
    public CarrierLabelFormat DefaultLabelFormat { get; private set; } = CarrierLabelFormat.Pdf4x6;

    /// <summary>Why the shipment can't be voided right now (its order's invoice is already out);
    /// null when it can.</summary>
    public ShipmentVoidBlocker? VoidBlocker { get; private set; }

    /// <summary>A recorded shipment that hasn't been delivered can be taken back by the people who ship.</summary>
    public bool CanVoid => CanShip && Detail?.Shipment.Status is ShipmentStatus.InTransit;

    /// <summary>Shipping staff record the carrier cost; accounting enters it from the WWEX bill.</summary>
    public bool CanEditCarrierCost =>
        User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Shipping) || User.IsInRole(AppRoles.Accounting);

    /// <summary>Who buys, prints and voids labels: the people who record shipments.</summary>
    public bool CanShip => User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Shipping);

    public string StatusText => Detail?.Shipment switch
    {
        null => string.Empty,
        { Status: ShipmentStatus.Pending, LabelState: ShipmentLabelState.Unconfirmed } => "Not shipped: waiting on the carrier",
        { Status: ShipmentStatus.InTransit } => "In transit",
        var shipment => shipment.Status.ToString()
    };

    public string Local(DateTime? utc) =>
        utc is { } value ? clock.ToLocal(value).ToString("MMM d, yyyy h:mm tt") : string.Empty;

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

    /// <summary>Under-run: a line shipped short and nothing more will ship. Moves the order to
    /// Shipped without a new shipment so its invoice can bill.</summary>
    public async Task<IActionResult> OnPostMarkOrderShippedAsync(CancellationToken cancellationToken)
    {
        if (!CanEditCarrierCost) return Forbid();
        if (!await LoadAsync(cancellationToken)) return NotFound();
        try
        {
            await shipmentService.MarkOrderShippedAsync(Detail!.Shipment.SalesOrderId, cancellationToken);
            TempData["ShipmentStatus"] = $"Order {OrderProgress?.OrderNumber} marked shipped.";
            return RedirectToPage(new { id = Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }

    /// <summary>Settles a label purchase the carrier never answered.</summary>
    public async Task<IActionResult> OnPostCheckCarrierAsync(CancellationToken cancellationToken)
    {
        if (!CanShip) return Forbid();
        try
        {
            var result = await carrierShipments.CheckUnconfirmedAsync(Id, cancellationToken);
            if (result.Outcome is LabelCheckOutcome.Discarded)
            {
                // The shipment is gone; the order is back where it can be shipped.
                TempData["RecordNotice"] = result.Message;
                return RedirectToPage("Record", new { salesOrderId = result.SalesOrderId });
            }

            TempData[result.Outcome is LabelCheckOutcome.Bought ? "ShipmentStatus" : "ShipmentWarning"] = result.Message;
        }
        catch (InvalidOperationException ex)
        {
            TempData["ShipmentWarning"] = ex.Message;
        }

        return RedirectToPage(new { id = Id });
    }

    /// <summary>Voids the shipment: the label with the carrier first when one was bought here,
    /// then the shipment itself, its jobs, its order and the invoice's shipping.</summary>
    public async Task<IActionResult> OnPostVoidAsync(string? voidReason, CancellationToken cancellationToken)
    {
        if (!CanShip) return Forbid();
        try
        {
            TempData["ShipmentStatus"] = await carrierShipments.VoidAsync(Id, voidReason, cancellationToken);
        }
        catch (Exception ex) when (ex is CarrierException or CarrierNotConfiguredException or InvalidOperationException)
        {
            TempData["ShipmentWarning"] = ex.Message;
        }

        return RedirectToPage(new { id = Id });
    }

    /// <summary>A bought label's file: PDFs open in the browser to print, ZPL downloads.</summary>
    public async Task<IActionResult> OnGetLabelAsync(Guid? packageId, CarrierLabelFormat format, CancellationToken cancellationToken)
    {
        if (!CanShip) return Forbid();
        try
        {
            var file = await carrierShipments.GetLabelAsync(Id, packageId, format, cancellationToken);
            if (format is CarrierLabelFormat.Zpl)
            {
                return File(file.Content, file.ContentType, file.FileName);
            }

            Response.Headers.ContentDisposition = $"inline; filename=\"{file.FileName}\"";
            return File(file.Content, file.ContentType);
        }
        catch (Exception ex) when (ex is CarrierException or CarrierNotConfiguredException or InvalidOperationException)
        {
            TempData["ShipmentWarning"] = ex.Message;
            return RedirectToPage(new { id = Id });
        }
    }

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        Detail = await shipmentService.GetDetailAsync(Id, cancellationToken);
        if (Detail is null) return false;
        Billing = await shipmentService.GetBillingContextAsync(Detail.Shipment.SalesOrderId, cancellationToken);
        OrderProgress = Detail.Shipment.CountsAsShipped
            ? await shipmentService.GetOrderShipProgressAsync(Detail.Shipment.SalesOrderId, cancellationToken)
            : null;

        CanInvoice = (User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Accounting))
            && Detail.Shipment.Status is ShipmentStatus.InTransit or ShipmentStatus.Delivered;
        if (Detail.Shipment.LabelState is ShipmentLabelState.Purchased)
        {
            DefaultLabelFormat = (await carrierSettings.GetViewAsync(cancellationToken)).DefaultLabelFormat;
        }

        if (CanVoid)
        {
            VoidBlocker = await shipmentService.GetVoidBlockerAsync(Id, cancellationToken);
        }

        return true;
    }

    public async Task<IActionResult> OnPostGenerateInvoiceAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Accounting)) return Forbid();
        var invoice = await invoiceService.CreateFromShipmentAsync(Id, cancellationToken);
        return RedirectToPage("/Invoices/Detail", new { id = invoice.Id });
    }
}
