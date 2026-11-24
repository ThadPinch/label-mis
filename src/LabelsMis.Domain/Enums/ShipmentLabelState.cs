namespace LabelsMis.Domain.Enums;

/// <summary>How far buying a carrier label for a shipment got.</summary>
public enum ShipmentLabelState
{
    /// <summary>No label was bought through LabelShop (tracking typed by hand, or no packages).</summary>
    None = 0,

    /// <summary>The booking was sent and no usable answer came back: the carrier may or may not
    /// have made the labels. Settled by Check with carrier on the shipment.</summary>
    Unconfirmed = 1,

    /// <summary>The carrier booked it; the tracking numbers are on the packages.</summary>
    Purchased = 2,

    /// <summary>The labels were voided with the carrier.</summary>
    Voided = 3
}
