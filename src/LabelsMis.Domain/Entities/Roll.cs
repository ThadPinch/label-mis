using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Entities;

public class Roll : EntityBase
{
    private readonly List<RollMovement> _movements = [];

    private Roll()
    {
    }

    public string RollBarcode { get; private set; } = string.Empty;
    public Guid StockId { get; private set; }
    public Stock Stock { get; private set; } = null!;
    public string SupplierLotNumber { get; private set; } = string.Empty;
    public decimal WidthIn { get; private set; }
    public decimal OriginalLengthLf { get; private set; }
    public decimal RemainingLengthLf { get; private set; }
    public DateTime ReceivedAt { get; private set; }
    public Guid? ReceiptId { get; private set; }
    public Receipt? Receipt { get; private set; }
    public string? Location { get; private set; }
    public RollStatus Status { get; private set; }
    public string? Notes { get; private set; }

    public IReadOnlyCollection<RollMovement> Movements => _movements;

    public static Roll Create(
        Guid id,
        string rollBarcode,
        Guid stockId,
        string supplierLotNumber,
        decimal widthIn,
        decimal lengthLf,
        DateTime receivedAt,
        Guid? receiptId,
        string? location,
        Guid createdById,
        DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(rollBarcode))
        {
            throw new ArgumentException("Roll barcode is required.", nameof(rollBarcode));
        }

        if (widthIn <= 0 || lengthLf <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lengthLf));
        }

        var roll = new Roll
        {
            RollBarcode = rollBarcode.Trim().ToUpperInvariant(),
            StockId = stockId,
            SupplierLotNumber = supplierLotNumber.Trim(),
            WidthIn = widthIn,
            OriginalLengthLf = lengthLf,
            RemainingLengthLf = lengthLf,
            ReceivedAt = receivedAt,
            ReceiptId = receiptId,
            Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim(),
            Status = RollStatus.Available
        };
        roll.SetCreated(id, createdById, createdAt);
        return roll;
    }

    public void Consume(decimal quantityLf, Guid modifiedById, DateTime modifiedAt)
    {
        if (quantityLf <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityLf));
        }

        if (quantityLf > RemainingLengthLf)
        {
            throw new InvalidOperationException("Cannot consume more than remaining length.");
        }

        RemainingLengthLf -= quantityLf;
        if (RemainingLengthLf <= 0)
        {
            Status = RollStatus.Depleted;
            RemainingLengthLf = 0;
        }

        SetModified(modifiedById, modifiedAt);
    }

    public void Stage(string location, Guid modifiedById, DateTime modifiedAt)
    {
        Status = RollStatus.Staged;
        Location = location.Trim();
        SetModified(modifiedById, modifiedAt);
    }

    public void MarkDepleted(Guid modifiedById, DateTime modifiedAt)
    {
        Status = RollStatus.Depleted;
        RemainingLengthLf = 0;
        SetModified(modifiedById, modifiedAt);
    }

    public void Scrap(Guid modifiedById, DateTime modifiedAt)
    {
        Status = RollStatus.Scrapped;
        RemainingLengthLf = 0;
        SetModified(modifiedById, modifiedAt);
    }

    public void AddMovement(RollMovement movement) => _movements.Add(movement);
}
