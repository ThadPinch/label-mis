using LabelsMis.Domain.Carriers;
using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Shipping;
using LabelsMis.Domain.ValueObjects;

namespace LabelsMis.Domain.Entities;

public class Shipment : EntityBase
{
    private readonly List<ShipmentPackage> _packages = [];
    private readonly List<ShipmentLine> _lines = [];

    private Shipment()
    {
    }

    public string ShipmentNumber { get; private set; } = string.Empty;
    public Guid SalesOrderId { get; private set; }
    public SalesOrder SalesOrder { get; private set; } = null!;
    public DateOnly ShipDate { get; private set; }
    public Carrier Carrier { get; private set; }
    public FedexServiceLevel? ServiceLevel { get; private set; }

    // Ship-from is a point-in-time snapshot of the company address from General Settings. The
    // snapshot columns are always populated; the address FK is left from the first label design
    // and is never set.
    public Guid? ShipFromAddressId { get; private set; }
    public Address? ShipFromAddress { get; private set; }
    public string? ShipFromName { get; private set; }
    public string? ShipFromStreet1 { get; private set; }
    public string? ShipFromStreet2 { get; private set; }
    public string? ShipFromCity { get; private set; }
    public string? ShipFromState { get; private set; }
    public string? ShipFromZip { get; private set; }
    public string? ShipFromCountry { get; private set; }

    public ShippingAddress ShipFromSnapshot => new(
        ShipFromName, ShipFromStreet1, ShipFromStreet2, ShipFromCity, ShipFromState, ShipFromZip, ShipFromCountry);

    public Guid? ShipToAddressId { get; private set; }
    public Address? ShipToAddress { get; private set; }
    public string? ShipToName { get; private set; }
    public string? ShipToStreet1 { get; private set; }
    public string? ShipToStreet2 { get; private set; }
    public string? ShipToCity { get; private set; }
    public string? ShipToState { get; private set; }
    public string? ShipToZip { get; private set; }
    public string? ShipToCountry { get; private set; }

    public ShippingAddress ShipToSnapshot => new(
        ShipToName, ShipToStreet1, ShipToStreet2, ShipToCity, ShipToState, ShipToZip, ShipToCountry);

    // What a carrier label needs beyond the address. Only set on shipments whose label was bought
    // through LabelShop.
    public string? ShipToContact { get; private set; }
    public string? ShipToPhone { get; private set; }
    public bool ShipToResidential { get; private set; }

    /// <summary>How far buying a carrier label for this shipment got. None for shipments recorded
    /// by hand.</summary>
    public ShipmentLabelState LabelState { get; private set; }

    /// <summary>Which of the carrier's worlds the label was bought in. A staging label is a
    /// sample: it can't ship and nothing is billed for it.</summary>
    public CarrierEnvironment? CarrierEnvironment { get; private set; }

    /// <summary>The carrier's id for the rate request the label was bought from. The labels, the
    /// void and a repeat of the booking all go by it.</summary>
    public string? CarrierQuoteId { get; private set; }
    public string? CarrierOfferId { get; private set; }

    /// <summary>The carrier's own number for the bought shipment (Worldwide Express: NUD…).</summary>
    public string? CarrierOrderNumber { get; private set; }
    public string? CarrierServiceCode { get; private set; }
    public string? CarrierServiceName { get; private set; }
    public string? CarrierBillToAccount { get; private set; }
    public string? CarrierBillToPostalCode { get; private set; }
    public DateTime? LabelPurchasedAt { get; private set; }

    /// <summary>When the carrier was last asked where this shipment's boxes are.</summary>
    public DateTime? TrackingCheckedAt { get; private set; }

    /// <summary>When a recorded shipment was taken back, by whom, and the reason they gave.</summary>
    public DateTime? VoidedAt { get; private set; }
    public Guid? VoidedById { get; private set; }
    public string? VoidReason { get; private set; }

    public const int MaxVoidReasonLength = 300;

    /// <summary>True for a label that is a sample from the carrier's staging environment.</summary>
    public bool IsSampleLabel =>
        LabelState != ShipmentLabelState.None && CarrierEnvironment == Enums.CarrierEnvironment.Staging;

    /// <summary>True while the shipment counts toward what its order has shipped.</summary>
    public bool CountsAsShipped => Status is not ShipmentStatus.Pending and not ShipmentStatus.Voided;

    public ShipmentStatus Status { get; private set; }
    public decimal TotalDeclaredValue { get; private set; }
    public BillingType BillingType { get; private set; }
    public string? BillingAccountNumber { get; private set; }
    public decimal TotalShippingCost { get; private set; }

    /// <summary>What the carrier actually charged for this shipment (the Worldwide Express
    /// figure), entered when the shipment is recorded or later on the shipment page. Null until
    /// it is known; billed shipping for carrier shipments is derived from it.</summary>
    public decimal? CarrierCost { get; private set; }

    /// <summary>What this shipment bills the customer for shipping, from the order's shipping
    /// method and <see cref="CarrierCost"/> (<see cref="ShippingBillingRules"/>), rounded to cents.
    /// Null while a carrier shipment's cost is unknown, and on shipments recorded before billed
    /// shipping existed.</summary>
    public decimal? BilledShipping { get; private set; }

    public IReadOnlyCollection<ShipmentPackage> Packages => _packages;
    public IReadOnlyCollection<ShipmentLine> Lines => _lines;

    public static Shipment CreatePending(
        Guid id,
        string shipmentNumber,
        Guid salesOrderId,
        DateOnly shipDate,
        Carrier carrier,
        FedexServiceLevel? serviceLevel,
        Guid? shipFromAddressId,
        ShippingAddress shipFrom,
        ShippingAddress shipTo,
        Guid? shipToAddressId,
        decimal totalDeclaredValue,
        BillingType billingType,
        string? billingAccountNumber,
        Guid createdById,
        DateTime createdAt,
        decimal? carrierCost = null)
    {
        ValidateCarrierCost(carrierCost);
        var snapshot = (shipFrom ?? ShippingAddress.Empty).Normalized();
        var toSnapshot = (shipTo ?? ShippingAddress.Empty).Normalized();
        var shipment = new Shipment
        {
            ShipmentNumber = shipmentNumber,
            SalesOrderId = salesOrderId,
            ShipDate = shipDate,
            Carrier = carrier,
            ServiceLevel = serviceLevel,
            ShipFromAddressId = shipFromAddressId,
            ShipFromName = snapshot.RecipientName,
            ShipFromStreet1 = snapshot.Street1,
            ShipFromStreet2 = snapshot.Street2,
            ShipFromCity = snapshot.City,
            ShipFromState = snapshot.State,
            ShipFromZip = snapshot.Zip,
            ShipFromCountry = snapshot.Country,
            ShipToAddressId = shipToAddressId,
            ShipToName = toSnapshot.RecipientName,
            ShipToStreet1 = toSnapshot.Street1,
            ShipToStreet2 = toSnapshot.Street2,
            ShipToCity = toSnapshot.City,
            ShipToState = toSnapshot.State,
            ShipToZip = toSnapshot.Zip,
            ShipToCountry = toSnapshot.Country,
            Status = ShipmentStatus.Pending,
            TotalDeclaredValue = totalDeclaredValue,
            BillingType = billingType,
            BillingAccountNumber = string.IsNullOrWhiteSpace(billingAccountNumber) ? null : billingAccountNumber.Trim(),
            CarrierCost = carrierCost
        };
        shipment.SetCreated(id, createdById, createdAt);
        return shipment;
    }

    public void AddPackage(ShipmentPackage package)
    {
        if (_packages.Count == 0 && package.PackageNumber != 1)
        {
            throw new InvalidOperationException("First package must be number 1.");
        }

        _packages.Add(package);
    }

    public void ReplacePackages(IEnumerable<ShipmentPackage> packages)
    {
        var list = packages.ToList();
        if (list.Count == 0)
        {
            throw new InvalidOperationException("Shipment must have at least one package.");
        }

        _packages.Clear();
        _packages.AddRange(list);
    }

    public void AddLine(ShipmentLine line) => _lines.Add(line);

    public void ReplaceLines(IEnumerable<ShipmentLine> lines)
    {
        _lines.Clear();
        _lines.AddRange(lines);
    }

    public void MarkInTransit(decimal shippingCost, Guid modifiedById, DateTime modifiedAt)
    {
        if (_packages.Count == 0)
        {
            throw new InvalidOperationException("Cannot ship without packages.");
        }

        if (_packages.Any(p => string.IsNullOrWhiteSpace(p.TrackingNumber)))
        {
            throw new InvalidOperationException("All packages must have tracking numbers.");
        }

        Status = ShipmentStatus.InTransit;
        TotalShippingCost = shippingCost;
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>
    /// Ships without any packages — the floor's "mark shipped" action for pickups and other
    /// hand-offs where nothing is packaged or tracked. Regular shipping goes through
    /// <see cref="MarkInTransit"/>, which requires tracked packages.
    /// </summary>
    public void MarkShippedWithoutPackages(Guid modifiedById, DateTime modifiedAt)
    {
        if (Status is not ShipmentStatus.Pending)
        {
            throw new InvalidOperationException("Only pending shipments can be marked shipped.");
        }

        Status = ShipmentStatus.InTransit;
        TotalShippingCost = 0m;
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>Records that a label is about to be bought for this pending shipment: which rate
    /// of which quote, on whose account, at what price. Saved before the carrier is asked, so a
    /// purchase whose answer never arrives can still be settled.</summary>
    public void StartLabelPurchase(
        CarrierEnvironment environment,
        string quoteId,
        string offerId,
        string serviceCode,
        string serviceName,
        CarrierBillTo billTo,
        decimal quotedCost,
        string? shipToContact,
        string? shipToPhone,
        bool shipToResidential,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        if (Status is not ShipmentStatus.Pending || LabelState is not ShipmentLabelState.None)
        {
            throw new InvalidOperationException("A label can only be bought for a shipment that has not shipped.");
        }

        if (_packages.Count == 0)
        {
            throw new InvalidOperationException("A label needs at least one package.");
        }

        if (string.IsNullOrWhiteSpace(quoteId) || string.IsNullOrWhiteSpace(offerId))
        {
            throw new ArgumentException("The carrier's quote and offer are required.");
        }

        ValidateCarrierCost(quotedCost);
        Carrier = Carrier.Ups;
        CarrierEnvironment = environment;
        CarrierQuoteId = quoteId.Trim();
        CarrierOfferId = offerId.Trim();
        CarrierServiceCode = serviceCode.Trim().ToUpperInvariant();
        CarrierServiceName = serviceName.Trim();
        CarrierBillToAccount = billTo.AccountNumber;
        CarrierBillToPostalCode = billTo.PostalCode;
        CarrierCost = quotedCost;
        ShipToContact = string.IsNullOrWhiteSpace(shipToContact) ? null : shipToContact.Trim();
        ShipToPhone = string.IsNullOrWhiteSpace(shipToPhone) ? null : shipToPhone.Trim();
        ShipToResidential = shipToResidential;
        LabelState = ShipmentLabelState.Unconfirmed;
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>The carrier booked it: every package has its tracking number. The shipment still
    /// has to be marked in transit (<see cref="MarkInTransit"/>).</summary>
    public void ConfirmLabelPurchase(string carrierOrderNumber, Guid modifiedById, DateTime modifiedAt)
    {
        if (LabelState is not ShipmentLabelState.Unconfirmed)
        {
            throw new InvalidOperationException("There is no label purchase waiting to be confirmed on this shipment.");
        }

        if (_packages.Any(p => string.IsNullOrWhiteSpace(p.TrackingNumber)))
        {
            throw new InvalidOperationException("Every package needs its tracking number before the purchase is confirmed.");
        }

        CarrierOrderNumber = string.IsNullOrWhiteSpace(carrierOrderNumber) ? null : carrierOrderNumber.Trim();
        LabelState = ShipmentLabelState.Purchased;
        LabelPurchasedAt = modifiedAt;
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>The account the carrier named when the label's rate was quoted.</summary>
    public CarrierBillTo? CarrierBillTo =>
        CarrierBillToAccount is null || CarrierBillToPostalCode is null
            ? null
            : new CarrierBillTo(CarrierBillToAccount, CarrierBillToPostalCode);

    /// <summary>Enters or corrects the carrier's actual charge (null clears it) and re-derives the
    /// billed shipping under the order's method and the shop's settings.</summary>
    public void SetCarrierCost(
        decimal? carrierCost,
        ShippingMethodType? methodType,
        ShippingBillingSettings settings,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        ValidateCarrierCost(carrierCost);
        CarrierCost = carrierCost;
        ComputeBilledShipping(methodType, settings);
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>Derives <see cref="BilledShipping"/> from the order's shipping method, the carrier
    /// cost and the shop's settings. Called when the shipment is recorded and on every edit.</summary>
    public void ComputeBilledShipping(ShippingMethodType? methodType, ShippingBillingSettings settings) =>
        BilledShipping = ShippingBillingRules.BilledShipping(methodType, CarrierCost, settings);

    private static void ValidateCarrierCost(decimal? carrierCost)
    {
        if (carrierCost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(carrierCost), "Carrier cost cannot be negative.");
        }
    }

    public void MarkDelivered(Guid modifiedById, DateTime modifiedAt)
    {
        Status = ShipmentStatus.Delivered;
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>Takes a recorded shipment back: it stops counting toward what the order has
    /// shipped. The caller voids the label with the carrier first when one was bought, and puts
    /// the jobs, the order and the invoice right afterwards.</summary>
    public void Void(string? reason, Guid voidedById, DateTime voidedAt)
    {
        if (Status is not ShipmentStatus.InTransit)
        {
            throw new InvalidOperationException(Status switch
            {
                ShipmentStatus.Voided => "This shipment is already voided.",
                ShipmentStatus.Delivered => "A delivered shipment can't be voided.",
                ShipmentStatus.Pending => "This shipment was never recorded as shipped, so there is nothing to void.",
                _ => "This shipment can't be voided."
            });
        }

        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Status = ShipmentStatus.Voided;
        if (LabelState is ShipmentLabelState.Purchased)
        {
            LabelState = ShipmentLabelState.Voided;
        }

        VoidedAt = voidedAt;
        VoidedById = voidedById;
        VoidReason = reason is { Length: > MaxVoidReasonLength } ? reason[..MaxVoidReasonLength] : reason;
        SetModified(voidedById, voidedAt);
    }

    /// <summary>Notes that the carrier was just asked where the boxes are. Once every box has
    /// been delivered the shipment is delivered. Returns true when that happened now.</summary>
    public bool ApplyTracking(Guid modifiedById, DateTime checkedAt)
    {
        TrackingCheckedAt = checkedAt;
        if (Status is not ShipmentStatus.InTransit
            || _packages.Count == 0
            || _packages.Any(p => p.TrackingStatus != CarrierTrackingStatus.Delivered))
        {
            return false;
        }

        Status = ShipmentStatus.Delivered;
        SetModified(modifiedById, checkedAt);
        return true;
    }
}
