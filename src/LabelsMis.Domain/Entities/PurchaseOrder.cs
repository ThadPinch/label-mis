using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Entities;

public class PurchaseOrder : EntityBase
{
    private readonly List<PurchaseOrderLine> _lines = [];

    private PurchaseOrder()
    {
    }

    public string PoNumber { get; private set; } = string.Empty;
    public Guid SupplierId { get; private set; }
    public Supplier Supplier { get; private set; } = null!;
    public DateTime OrderedAt { get; private set; }
    public DateOnly? ExpectedAt { get; private set; }
    public PurchaseOrderStatus Status { get; private set; }
    public string? Notes { get; private set; }

    public IReadOnlyCollection<PurchaseOrderLine> Lines => _lines;

    public static PurchaseOrder CreateDraft(
        Guid id,
        string poNumber,
        Guid supplierId,
        DateTime orderedAt,
        DateOnly? expectedAt,
        string? notes,
        Guid createdById,
        DateTime createdAt)
    {
        var po = new PurchaseOrder
        {
            PoNumber = poNumber,
            SupplierId = supplierId,
            OrderedAt = orderedAt,
            ExpectedAt = expectedAt,
            Status = PurchaseOrderStatus.Draft,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };
        po.SetCreated(id, createdById, createdAt);
        return po;
    }

    public void MarkSent(Guid modifiedById, DateTime modifiedAt)
    {
        if (Status is not PurchaseOrderStatus.Draft)
        {
            throw new InvalidOperationException("Only draft POs can be sent.");
        }

        Status = PurchaseOrderStatus.Sent;
        SetModified(modifiedById, modifiedAt);
    }

    public void UpdateReceiptStatus(Guid modifiedById, DateTime modifiedAt)
    {
        if (_lines.All(l => l.QuantityReceivedLf >= l.QuantityLf))
        {
            Status = PurchaseOrderStatus.Received;
        }
        else if (_lines.Any(l => l.QuantityReceivedLf > 0))
        {
            Status = PurchaseOrderStatus.PartiallyReceived;
        }

        SetModified(modifiedById, modifiedAt);
    }

    public void AddLine(PurchaseOrderLine line) => _lines.Add(line);
    public void ReplaceLines(IEnumerable<PurchaseOrderLine> lines)
    {
        _lines.Clear();
        _lines.AddRange(lines);
    }
}
