using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Entities;

public class ShipmentPackage : EntityBase
{
    private readonly List<TrackingEvent> _trackingEvents = [];

    private ShipmentPackage()
    {
    }

    public Guid ShipmentId { get; private set; }
    public Shipment Shipment { get; private set; } = null!;
    public int PackageNumber { get; private set; }
    public decimal WeightLb { get; private set; }
    public decimal LengthIn { get; private set; }
    public decimal WidthIn { get; private set; }
    public decimal HeightIn { get; private set; }
    public string? TrackingNumber { get; private set; }
    public string? LabelUrl { get; private set; }
    public decimal DeclaredValue { get; private set; }
    public decimal ShippingCost { get; private set; }

    /// <summary>The carrier's id for this box in the rate request; the booking answers with the
    /// same key, which is how a tracking number finds its box.</summary>
    public string? CarrierBoxKey { get; private set; }

    /// <summary>The carrier's id for the bought label of this box; tracking answers go by it.</summary>
    public string? CarrierPackageId { get; private set; }

    /// <summary>The carrier's last word on where this box is. Null until it has been asked.</summary>
    public CarrierTrackingStatus? TrackingStatus { get; private set; }

    public IReadOnlyCollection<TrackingEvent> TrackingEvents => _trackingEvents;

    public static ShipmentPackage Create(
        Guid id,
        Guid shipmentId,
        int packageNumber,
        decimal weightLb,
        decimal lengthIn,
        decimal widthIn,
        decimal heightIn,
        decimal declaredValue,
        Guid createdById,
        DateTime createdAt)
    {
        if (packageNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(packageNumber));
        }

        if (weightLb <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(weightLb));
        }

        var package = new ShipmentPackage
        {
            ShipmentId = shipmentId,
            PackageNumber = packageNumber,
            WeightLb = weightLb,
            LengthIn = lengthIn,
            WidthIn = widthIn,
            HeightIn = heightIn,
            DeclaredValue = declaredValue
        };
        package.SetCreated(id, createdById, createdAt);
        return package;
    }

    public void SetLabel(string trackingNumber, string labelUrl, decimal shippingCost, Guid modifiedById, DateTime modifiedAt)
    {
        TrackingNumber = trackingNumber.Trim();
        LabelUrl = labelUrl;
        ShippingCost = shippingCost;
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>Records a manually entered tracking number (no carrier label is generated).</summary>
    public void SetManualTracking(string trackingNumber, decimal shippingCost, Guid modifiedById, DateTime modifiedAt)
    {
        if (string.IsNullOrWhiteSpace(trackingNumber))
        {
            throw new ArgumentException("Tracking number is required.", nameof(trackingNumber));
        }

        TrackingNumber = trackingNumber.Trim();
        ShippingCost = shippingCost;
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>Before a label is bought: which box of the carrier's quote this is and what the
    /// quote priced it at.</summary>
    public void SetCarrierQuote(string boxKey, decimal quotedCost, Guid modifiedById, DateTime modifiedAt)
    {
        if (string.IsNullOrWhiteSpace(boxKey))
        {
            throw new ArgumentException("The carrier's box key is required.", nameof(boxKey));
        }

        if (quotedCost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quotedCost));
        }

        CarrierBoxKey = boxKey.Trim();
        ShippingCost = quotedCost;
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>The carrier bought the label for this box.</summary>
    public void SetCarrierLabel(string trackingNumber, string? carrierPackageId, Guid modifiedById, DateTime modifiedAt)
    {
        if (string.IsNullOrWhiteSpace(trackingNumber))
        {
            throw new ArgumentException("Tracking number is required.", nameof(trackingNumber));
        }

        TrackingNumber = trackingNumber.Trim();
        CarrierPackageId = string.IsNullOrWhiteSpace(carrierPackageId) ? null : carrierPackageId.Trim();
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>Notes what the carrier last said about this box. Returns true when it is news.</summary>
    public bool SetTrackingStatus(CarrierTrackingStatus status)
    {
        var changed = TrackingStatus != status;
        TrackingStatus = status;
        return changed;
    }

    public void AddTrackingEvent(TrackingEvent trackingEvent) => _trackingEvents.Add(trackingEvent);
}
