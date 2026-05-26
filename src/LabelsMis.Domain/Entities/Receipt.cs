using LabelsMis.Domain.Common;

namespace LabelsMis.Domain.Entities;

public class Receipt : EntityBase
{
    private Receipt()
    {
    }

    public Guid PoLineId { get; private set; }
    public PurchaseOrderLine PoLine { get; private set; } = null!;
    public DateTime ReceivedAt { get; private set; }
    public decimal QuantityLf { get; private set; }
    public string? Notes { get; private set; }

    public static Receipt Create(
        Guid id,
        Guid poLineId,
        DateTime receivedAt,
        decimal quantityLf,
        string? notes,
        Guid receivedById,
        DateTime createdAt)
    {
        if (quantityLf <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityLf));
        }

        var receipt = new Receipt
        {
            PoLineId = poLineId,
            ReceivedAt = receivedAt,
            QuantityLf = quantityLf,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };
        receipt.SetCreated(id, receivedById, createdAt);
        return receipt;
    }
}
