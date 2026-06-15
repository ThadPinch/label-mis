using LabelsMis.Domain.Enums;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Shipments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Shipments;

[Authorize(Policy = TransactionPolicies.ShippingEdit)]
public class RecordModel(ShipmentService shipmentService) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid SalesOrderId { get; set; }
    [BindProperty] public RecordShipmentInput Input { get; set; } = new();

    public ManualShipmentOrderView? Order { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Order = await shipmentService.GetOrderForManualShipmentAsync(SalesOrderId, cancellationToken);
        if (Order is null) return NotFound();

        Input = new RecordShipmentInput
        {
            ShipDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Carrier = Carrier.Fedex,
            ShipToAddressId = Order.DefaultShipToAddressId ?? Guid.Empty,
            Lines = Order.Lines.Where(l => l.IsReady).Select(l => new RecordLineInput
            {
                SalesOrderLineId = l.SalesOrderLineId,
                JobId = l.JobId,
                Include = true,
                Quantity = l.QuantityRemaining
            }).ToList(),
            Packages = [new RecordPackageInput()]
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var input = new ManualShipmentInput(
            Input.ShipDate,
            Input.Carrier,
            Input.ShipToAddressId,
            Input.Lines
                .Where(l => l.Include && l.Quantity > 0)
                .Select(l => new ManualShipmentLineInput(l.SalesOrderLineId, l.JobId, l.Quantity))
                .ToList(),
            Input.Packages
                .Where(p => !string.IsNullOrWhiteSpace(p.TrackingNumber))
                .Select(p => new ManualPackageInput(
                    p.WeightLb, p.LengthIn, p.WidthIn, p.HeightIn, p.DeclaredValue, p.TrackingNumber!.Trim(), p.ShippingCost))
                .ToList());

        try
        {
            var shipment = await shipmentService.CreateManualShipmentAsync(SalesOrderId, input, cancellationToken);
            return RedirectToPage("Detail", new { id = shipment.Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            Order = await shipmentService.GetOrderForManualShipmentAsync(SalesOrderId, cancellationToken);
            if (Order is null) return NotFound();
            return Page();
        }
    }
}

public class RecordShipmentInput
{
    public DateOnly ShipDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public Carrier Carrier { get; set; } = Carrier.Fedex;
    public Guid ShipToAddressId { get; set; }
    public List<RecordLineInput> Lines { get; set; } = [];
    public List<RecordPackageInput> Packages { get; set; } = [new()];
}

public class RecordLineInput
{
    public Guid SalesOrderLineId { get; set; }
    public Guid? JobId { get; set; }
    public bool Include { get; set; }
    public int Quantity { get; set; }
}

public class RecordPackageInput
{
    public decimal WeightLb { get; set; } = 1;
    public decimal LengthIn { get; set; } = 12;
    public decimal WidthIn { get; set; } = 12;
    public decimal HeightIn { get; set; } = 6;
    public decimal DeclaredValue { get; set; }
    public string? TrackingNumber { get; set; }
    public decimal ShippingCost { get; set; }
}
