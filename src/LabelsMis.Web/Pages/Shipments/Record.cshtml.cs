using System.Globalization;
using LabelsMis.Domain.Carriers;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Shipping;
using LabelsMis.Domain.ValueObjects;
using LabelsMis.Web.Authorization;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.Carriers;
using LabelsMis.Web.Services.Shipments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LabelsMis.Web.Pages.Shipments;

/// <summary>Records a shipment for an order one of two ways: by buying the UPS label here (boxes →
/// Get rates → Buy label; tracking numbers and the carrier cost fill themselves in), or by typing
/// tracking numbers that came from somewhere else. The first needs the carrier keys saved in
/// Setup → Shipping carrier; without them the page is the hand-entry form alone.</summary>
[Authorize(Policy = TransactionPolicies.ShippingEdit)]
public class RecordModel(
    ShipmentService shipmentService,
    CarrierShipmentService carrierShipments,
    CarrierSettingsService carrierSettings,
    ClientClock clock) : PageModel
{
    public const string ModeLabel = "label";
    public const string ModeManual = "manual";

    [BindProperty(SupportsGet = true)] public Guid SalesOrderId { get; set; }
    [BindProperty] public RecordShipmentInput Input { get; set; } = new();

    public ManualShipmentOrderView? Order { get; private set; }
    public ShipmentBillingContext? Billing { get; private set; }

    /// <summary>The carrier account as Setup → Shipping carrier holds it.</summary>
    public CarrierSettingsView? CarrierAccount { get; private set; }

    /// <summary>The rates on screen, once Get rates has been pressed.</summary>
    public LabelQuote? Quote { get; private set; }

    /// <summary>A label purchase for this order the carrier never answered; it is settled first.</summary>
    public UnconfirmedShipmentRef? Unconfirmed { get; private set; }

    public bool CanBuyLabel => CarrierAccount is { IsReady: true };

    public bool LabelMode => CanBuyLabel && string.Equals(Input.Mode, ModeLabel, StringComparison.OrdinalIgnoreCase);

    /// <summary>What the customer is billed for shipping at this carrier cost, under the order's method.</summary>
    public decimal? BilledFor(decimal carrierCost) =>
        Billing is null ? null : ShippingBillingRules.BilledShipping(Billing.ShippingMethodType, carrierCost, Billing.Settings);

    /// <summary>"Wed, Nov 25 by 10:30 AM (1 day)".</summary>
    public static string Arrives(QuotedRate rate)
    {
        var text = rate.DeliveryDate is { } date ? date.ToString("ddd, MMM d", CultureInfo.InvariantCulture) : string.Empty;
        if (TimeOnly.TryParse(rate.DeliveryBy, CultureInfo.InvariantCulture, out var by) && by.Hour is > 0 and < 20)
        {
            text = $"{text} by {by.ToString("h:mm tt", CultureInfo.InvariantCulture)}".Trim();
        }

        if (rate.TransitDays is { } days)
        {
            var transit = days == 1 ? "1 day" : $"{days} days";
            text = text.Length == 0 ? transit : $"{text} ({transit})";
        }

        return text.Length == 0 ? "—" : text;
    }

    /// <summary>"billed at 7 lb · Freight $15.04 · Fuel $3.95".</summary>
    public static string Details(QuotedRate rate)
    {
        var parts = new List<string>();
        if (rate.BilledWeightLb is { } weight)
        {
            parts.Add($"billed at {weight.ToString("0.##", CultureInfo.InvariantCulture)} lb");
        }

        parts.AddRange(rate.Charges.Where(c => c.Amount != 0m).Select(c => $"{c.Name} {CarrierGateway.Money(c.Amount)}"));
        return string.Join(" · ", parts);
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await LoadAsync(cancellationToken)) return NotFound();

        var shipTo = Order!.DefaultShipTo;
        var buysLabel = CanBuyLabel && Order.ShippingMethodType is null or ShippingMethodType.Carrier;
        var contact = buysLabel
            ? await shipmentService.GetShipToContactAsync(SalesOrderId, cancellationToken)
            : new ShipToContactDefault(null, null);
        var preset = CarrierAccount?.BoxPresets.FirstOrDefault();
        Input = new RecordShipmentInput
        {
            Mode = buysLabel ? ModeLabel : ModeManual,
            ShipDate = clock.Today,
            Carrier = Carrier.Ups,
            ShipToName = shipTo.RecipientName,
            ShipToStreet1 = shipTo.Street1,
            ShipToStreet2 = shipTo.Street2,
            ShipToCity = shipTo.City,
            ShipToState = shipTo.State,
            ShipToZip = shipTo.Zip,
            ShipToCountry = shipTo.Country,
            ShipToContact = contact.Name,
            ShipToPhone = contact.Phone,
            Lines = Order.Lines.Where(l => l.IsReady).Select(l => new RecordLineInput
            {
                SalesOrderLineId = l.SalesOrderLineId,
                JobId = l.JobId,
                Include = true,
                Quantity = l.QuantityRemaining
            }).ToList(),

            // A label is priced by weight and size, so the weight starts empty and the size comes
            // from the shop's first usual box. A hand-recorded package keeps its old defaults.
            Packages = buysLabel
                ? [new RecordPackageInput { WeightLb = null, LengthIn = preset?.LengthIn, WidthIn = preset?.WidthIn, HeightIn = preset?.HeightIn }]
                : [new RecordPackageInput()]
        };

        return Page();
    }

    /// <summary>Records a shipment whose tracking numbers were typed in.</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Input.Mode = ModeManual;
        var input = new ManualShipmentInput(
            Input.ShipDate,
            Input.Carrier,
            ShipTo(),
            ShipLines(),
            Input.Packages
                .Where(p => !string.IsNullOrWhiteSpace(p.TrackingNumber))
                .Select(p => new ManualPackageInput(
                    p.WeightLb ?? 1m, p.LengthIn ?? 12m, p.WidthIn ?? 12m, p.HeightIn ?? 6m, p.DeclaredValue ?? 0m,
                    p.TrackingNumber!.Trim(), p.ShippingCost ?? 0m))
                .ToList(),
            Input.CarrierCost);

        try
        {
            var shipment = await shipmentService.CreateManualShipmentAsync(SalesOrderId, input, cancellationToken);
            return RedirectToPage("Detail", new { id = shipment.Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await LoadAsync(cancellationToken) ? Page() : NotFound();
        }
    }

    /// <summary>Get rates: prices the boxes on every UPS service and shows them. Buys nothing.</summary>
    public async Task<IActionResult> OnPostRatesAsync(CancellationToken cancellationToken)
    {
        if (!await LoadAsync(cancellationToken)) return NotFound();
        Input.Mode = ModeLabel;
        Input.QuoteToken = null;
        Input.OfferId = null;
        try
        {
            Quote = await carrierShipments.GetRatesAsync(SalesOrderId, LabelRequest(), cancellationToken);
            Input.QuoteToken = Quote.Token;
            Input.OfferId = (Quote.Rates.FirstOrDefault(r => r.ServiceCode == CarrierAccount?.DefaultServiceCode)
                ?? Quote.Rates.FirstOrDefault())?.OfferId;
        }
        catch (LabelRequestException ex)
        {
            foreach (var problem in ex.Problems)
            {
                ModelState.AddModelError(string.Empty, problem);
            }
        }
        catch (Exception ex) when (ex is CarrierException or CarrierNotConfiguredException or InvalidOperationException)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
        }

        return Page();
    }

    /// <summary>Buy label: buys the picked service of the rates on screen and records the shipment.</summary>
    public async Task<IActionResult> OnPostBuyAsync(CancellationToken cancellationToken)
    {
        if (!await LoadAsync(cancellationToken)) return NotFound();
        Input.Mode = ModeLabel;
        try
        {
            var result = await carrierShipments.BuyLabelAsync(
                SalesOrderId, LabelRequest(), ShipLines(), Input.QuoteToken, Input.OfferId, cancellationToken);
            TempData[result.Confirmed ? "ShipmentStatus" : "ShipmentWarning"] = result.Message;
            return RedirectToPage("Detail", new { id = result.ShipmentId });
        }
        catch (StaleQuoteException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            Input.QuoteToken = null;
            Input.OfferId = null;
        }
        catch (LabelRequestException ex)
        {
            foreach (var problem in ex.Problems)
            {
                ModelState.AddModelError(string.Empty, problem);
            }

            Input.QuoteToken = null;
            Input.OfferId = null;
        }
        catch (Exception ex) when (ex is CarrierException or CarrierNotConfiguredException or InvalidOperationException)
        {
            // The rates are still good (an item wasn't ticked, or the carrier said no): keep them on screen.
            ModelState.AddModelError(string.Empty, ex.Message);
            Quote = carrierShipments.TryReadQuote(Input.QuoteToken);
            if (Quote is null)
            {
                Input.QuoteToken = null;
                Input.OfferId = null;
            }
        }

        // A refused purchase may have changed what is waiting on the carrier.
        Unconfirmed = await shipmentService.GetUnconfirmedShipmentAsync(SalesOrderId, cancellationToken);
        return Page();
    }

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        Order = await shipmentService.GetOrderForManualShipmentAsync(SalesOrderId, cancellationToken);
        if (Order is null) return false;
        Billing = await shipmentService.GetBillingContextAsync(SalesOrderId, cancellationToken);
        CarrierAccount = await carrierSettings.GetViewAsync(cancellationToken);
        Unconfirmed = await shipmentService.GetUnconfirmedShipmentAsync(SalesOrderId, cancellationToken);
        return true;
    }

    private ShippingAddress ShipTo() => new(
        Input.ShipToName, Input.ShipToStreet1, Input.ShipToStreet2,
        Input.ShipToCity, Input.ShipToState, Input.ShipToZip, Input.ShipToCountry);

    private List<ManualShipmentLineInput> ShipLines() =>
        Input.Lines
            .Where(l => l.Include && l.Quantity > 0)
            .Select(l => new ManualShipmentLineInput(l.SalesOrderLineId, l.JobId, l.Quantity))
            .ToList();

    private LabelRequestInput LabelRequest() => new(
        Input.ShipDate,
        ShipTo(),
        Input.ShipToContact,
        Input.ShipToPhone,
        Input.ShipToResidential,
        Input.Packages
            .Select(p => new LabelBoxInput(p.WeightLb ?? 0m, p.LengthIn ?? 0m, p.WidthIn ?? 0m, p.HeightIn ?? 0m, p.DeclaredValue ?? 0m))
            .ToList());
}

public class RecordShipmentInput
{
    /// <summary>"label" to buy the UPS label here, "manual" to type tracking numbers.</summary>
    public string Mode { get; set; } = RecordModel.ModeManual;

    public DateOnly ShipDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public Carrier Carrier { get; set; } = Carrier.Ups;

    /// <summary>The carrier's charge for the whole shipment (WWEX); blank = not known yet.</summary>
    [System.ComponentModel.DataAnnotations.Range(0, 999999)]
    [System.ComponentModel.DataAnnotations.Display(Name = "Carrier cost")]
    public decimal? CarrierCost { get; set; }

    public string? ShipToName { get; set; }
    public string? ShipToStreet1 { get; set; }
    public string? ShipToStreet2 { get; set; }
    public string? ShipToCity { get; set; }
    public string? ShipToState { get; set; }
    public string? ShipToZip { get; set; }
    public string? ShipToCountry { get; set; }

    // Asked for only when a label is bought: who the driver calls, and whether it is a home.
    public string? ShipToContact { get; set; }
    public string? ShipToPhone { get; set; }
    public bool ShipToResidential { get; set; }

    /// <summary>The rates on screen, sealed by the server, and the service picked from them.</summary>
    public string? QuoteToken { get; set; }
    public string? OfferId { get; set; }

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

/// <summary>One package row. Blank is allowed everywhere: a hand-recorded package falls back to
/// 1 lb in a 12 × 12 × 6 box as it always has, and a label refuses a box with no weight.</summary>
public class RecordPackageInput
{
    public decimal? WeightLb { get; set; } = 1;
    public decimal? LengthIn { get; set; } = 12;
    public decimal? WidthIn { get; set; } = 12;
    public decimal? HeightIn { get; set; } = 6;
    public decimal? DeclaredValue { get; set; } = 0;
    public string? TrackingNumber { get; set; }
    public decimal? ShippingCost { get; set; } = 0;
}
