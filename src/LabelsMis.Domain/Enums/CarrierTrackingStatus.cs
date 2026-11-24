namespace LabelsMis.Domain.Enums;

/// <summary>Where a box stands with the carrier, in the carrier's own order of events.</summary>
public enum CarrierTrackingStatus
{
    Unknown = 0,
    LabelCreated = 1,
    PendingPickup = 2,
    InTransit = 3,
    OutForDelivery = 4,
    Delivered = 5,
    Exception = 6,
    Voided = 7
}
