using LabelsMis.Domain.Common;

namespace LabelsMis.Domain.Entities;

public class ShipmentLine : EntityBase
{
    private ShipmentLine()
    {
    }

    public Guid ShipmentId { get; private set; }
    public Shipment Shipment { get; private set; } = null!;
    public Guid SalesOrderLineId { get; private set; }
    public SalesOrderLine SalesOrderLine { get; private set; } = null!;
    public Guid? JobId { get; private set; }
    public Job? Job { get; private set; }
    public int QuantityShipped { get; private set; }

    public static ShipmentLine Create(
        Guid id,
        Guid shipmentId,
        Guid salesOrderLineId,
        Guid? jobId,
        int quantityShipped,
        Guid createdById,
        DateTime createdAt)
    {
        if (quantityShipped <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityShipped));
        }

        var line = new ShipmentLine
        {
            ShipmentId = shipmentId,
            SalesOrderLineId = salesOrderLineId,
            JobId = jobId,
            QuantityShipped = quantityShipped
        };
        line.SetCreated(id, createdById, createdAt);
        return line;
    }
}
