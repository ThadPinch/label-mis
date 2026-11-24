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

    /// <summary>The carrier's own status word for the event (CREATED, IN_TRANSIT, DELIVERED …).
    /// Set on events read from the carrier: with the box and the time it identifies the event,
    /// so the same one is never stored twice.</summary>
    public string? CarrierCode { get; private set; }

    public const int MaxCarrierCodeLength = 40;

    public static TrackingEvent Create(
        Guid id,
        Guid shipmentPackageId,
        DateTime eventAt,
        string statusDescription,
        string? location,
        string? rawPayload,
        Guid createdById,
        DateTime createdAt,
        string? carrierCode = null)
    {
        carrierCode = string.IsNullOrWhiteSpace(carrierCode) ? null : carrierCode.Trim().ToUpperInvariant();
        var trackingEvent = new TrackingEvent
        {
            ShipmentPackageId = shipmentPackageId,
            EventAt = eventAt,
            StatusDescription = statusDescription.Trim(),
            Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim(),
            RawPayload = rawPayload,
            CarrierCode = carrierCode is { Length: > MaxCarrierCodeLength } ? carrierCode[..MaxCarrierCodeLength] : carrierCode
        };
        trackingEvent.SetCreated(id, createdById, createdAt);
        return trackingEvent;
    }
}
