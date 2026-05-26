using LabelsMis.Domain.Common;

namespace LabelsMis.Domain.Entities;

public class TrackingEvent : EntityBase
{
    private TrackingEvent()
    {
    }

    public Guid ShipmentPackageId { get; private set; }
    public ShipmentPackage ShipmentPackage { get; private set; } = null!;
    public DateTime EventAt { get; private set; }
    public string StatusDescription { get; private set; } = string.Empty;
    public string? Location { get; private set; }
    public string? RawPayload { get; private set; }

    public static TrackingEvent Create(
        Guid id,
        Guid shipmentPackageId,
        DateTime eventAt,
        string statusDescription,
        string? location,
        string? rawPayload,
        Guid createdById,
        DateTime createdAt)
    {
        var trackingEvent = new TrackingEvent
        {
            ShipmentPackageId = shipmentPackageId,
            EventAt = eventAt,
            StatusDescription = statusDescription.Trim(),
            Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim(),
            RawPayload = rawPayload
        };
        trackingEvent.SetCreated(id, createdById, createdAt);
        return trackingEvent;
    }
}
