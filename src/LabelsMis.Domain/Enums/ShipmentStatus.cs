namespace LabelsMis.Domain.Enums;

public enum ShipmentStatus
{
    Pending = 0,
    InTransit = 1,
    Delivered = 2,
    Exception = 3,
    Returned = 4,

    /// <summary>Taken back after it was recorded (a label bought by mistake, a shipment recorded
    /// on the wrong order). It no longer counts toward what the order has shipped.</summary>
    Voided = 5
}
