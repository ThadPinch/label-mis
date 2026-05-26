using LabelsMis.Domain.Enums;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services.Shipments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LabelsMis.Web.Pages.Shipments;

[Authorize(Policy = TransactionPolicies.ShippingEdit)]
public class CreateModel(ShipmentService shipmentService) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid? SalesOrderId { get; set; }
    [BindProperty] public DateOnly ShipDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    [BindProperty] public FedexServiceLevel ServiceLevel { get; set; } = FedexServiceLevel.FedexGround;
    [BindProperty] public Guid ShipToAddressId { get; set; }
    [BindProperty] public BillingType BillingType { get; set; } = BillingType.Sender;
    [BindProperty] public string? BillingAccountNumber { get; set; }
    [BindProperty] public List<ShipmentLineInput> Lines { get; set; } = [];
    [BindProperty] public ShipmentPackageInput Package { get; set; } = new(1, 12, 12, 6, 0);

    public Domain.Entities.SalesOrder? Order { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!SalesOrderId.HasValue) return BadRequest("salesOrderId is required.");
        Order = await shipmentService.GetSalesOrderForCreateAsync(SalesOrderId.Value, cancellationToken);
        if (Order is null) return NotFound();

        var shipTo = Order.Customer.Addresses
            .Where(a => a.AddressType == AddressType.Shipping)
            .OrderByDescending(a => a.IsDefault)
            .FirstOrDefault();
        if (shipTo is not null) ShipToAddressId = shipTo.Id;

        Lines = Order.Lines.OrderBy(l => l.LineNumber).Select(l => new ShipmentLineInput(l.Id, null, l.Quantity)).ToList();
        Package = new ShipmentPackageInput(1, 12, 12, 6, Order.Lines.Sum(l => l.LineTotal));
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!SalesOrderId.HasValue) return BadRequest();
        try
        {
            var shipment = await shipmentService.CreateAsync(
                SalesOrderId.Value,
                new CreateShipmentInput(ShipDate, ServiceLevel, ShipToAddressId, BillingType, BillingAccountNumber, Lines, [Package]),
                cancellationToken);
            return RedirectToPage("Detail", new { id = shipment.Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            Order = await shipmentService.GetSalesOrderForCreateAsync(SalesOrderId.Value, cancellationToken);
            return Page();
        }
    }

    public IEnumerable<SelectListItem> ShipToOptions =>
        Order?.Customer.Addresses
            .Where(a => a.AddressType == AddressType.Shipping)
            .Select(a => new SelectListItem($"{a.Street1}, {a.City}", a.Id.ToString())) ?? [];
}
