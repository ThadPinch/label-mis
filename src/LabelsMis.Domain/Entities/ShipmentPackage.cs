using LabelsMis.Domain.Common;

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

    public void AddTrackingEvent(TrackingEvent trackingEvent) => _trackingEvents.Add(trackingEvent);
}
