using LabelsMis.Domain.Common;

namespace LabelsMis.Domain.Entities;

public class PurchaseOrderLine : EntityBase
{
    private readonly List<Receipt> _receipts = [];

    private PurchaseOrderLine()
    {
    }

    public Guid PurchaseOrderId { get; private set; }
    public PurchaseOrder PurchaseOrder { get; private set; } = null!;
    public int LineNumber { get; private set; }
    public Guid StockId { get; private set; }
    public Stock Stock { get; private set; } = null!;
    public decimal QuantityLf { get; private set; }
    public decimal UnitCost { get; private set; }
    public decimal LineTotal { get; private set; }
    public decimal QuantityReceivedLf { get; private set; }

    public IReadOnlyCollection<Receipt> Receipts => _receipts;

    public static PurchaseOrderLine Create(
        Guid id,
        Guid purchaseOrderId,
        int lineNumber,
        Guid stockId,
        decimal quantityLf,
        decimal unitCost,
        Guid createdById,
        DateTime createdAt)
    {
        if (quantityLf <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityLf));
        }

        var line = new PurchaseOrderLine
        {
            PurchaseOrderId = purchaseOrderId,
            LineNumber = lineNumber,
            StockId = stockId,
            QuantityLf = quantityLf,
            UnitCost = unitCost,
            LineTotal = unitCost * quantityLf
        };
        line.SetCreated(id, createdById, createdAt);
        return line;
    }

    public void RecordReceipt(decimal quantityLf, Guid modifiedById, DateTime modifiedAt)
    {
        if (QuantityReceivedLf + quantityLf > QuantityLf)
        {
            throw new InvalidOperationException("Receipt exceeds ordered quantity.");
        }

        QuantityReceivedLf += quantityLf;
        SetModified(modifiedById, modifiedAt);
    }

    public void AddReceipt(Receipt receipt) => _receipts.Add(receipt);
}
