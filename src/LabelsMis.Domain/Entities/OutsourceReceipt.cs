using LabelsMis.Domain.Common;

namespace LabelsMis.Domain.Entities;

/// <summary>One delivery from the vendor against an <see cref="OutsourcedItem"/> — partial receipts
/// are normal, so an item accumulates receipts until it is marked complete.</summary>
public class OutsourceReceipt : EntityBase
{
    private OutsourceReceipt()
    {
    }

    public Guid OutsourcedItemId { get; private set; }
    public OutsourcedItem OutsourcedItem { get; private set; } = null!;
    public DateOnly ReceivedOn { get; private set; }
    public int Quantity { get; private set; }
    public string? Notes { get; private set; }

    public static OutsourceReceipt Create(
        Guid id,
        Guid outsourcedItemId,
        DateOnly receivedOn,
        int quantity,
        string? notes,
        Guid createdById,
        DateTime createdAt)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Received quantity must be greater than zero.");
        }

        var receipt = new OutsourceReceipt
        {
            OutsourcedItemId = outsourcedItemId,
            ReceivedOn = receivedOn,
            Quantity = quantity,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };
        receipt.SetCreated(id, createdById, createdAt);
        return receipt;
    }
}
