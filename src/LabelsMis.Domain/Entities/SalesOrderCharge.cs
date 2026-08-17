using LabelsMis.Domain.Common;

namespace LabelsMis.Domain.Entities;

/// <summary>A flat, non-label item on a sales order: one-time charges (die creation, design time,
/// plates) and outsourced goods (promo items, print, wide format). Charges are invoiced with the
/// order and shown on the job ticket; no production job is ever scheduled for them, but an
/// outsourced charge is tracked and received on the production Outsourced page.</summary>
public class SalesOrderCharge : EntityBase
{
    private SalesOrderCharge()
    {
    }

    public Guid SalesOrderId { get; private set; }
    public SalesOrder SalesOrder { get; private set; } = null!;
    public int LineNumber { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public Guid? SourceEstimateChargeId { get; private set; }
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal LineTotal { get; private set; }

    /// <summary>Set when this charge is bought from an outside vendor.</summary>
    public OutsourcedItem? OutsourcedItem { get; private set; }
    public bool IsOutsourced => OutsourcedItem is not null;

    public static SalesOrderCharge Create(
        Guid id,
        Guid salesOrderId,
        int lineNumber,
        string description,
        Guid? sourceEstimateChargeId,
        int quantity,
        decimal unitPrice,
        Guid createdById,
        DateTime createdAt)
    {
        Validate(lineNumber, description, quantity, unitPrice);

        var charge = new SalesOrderCharge
        {
            SalesOrderId = salesOrderId,
            LineNumber = lineNumber,
            Description = description.Trim(),
            SourceEstimateChargeId = sourceEstimateChargeId,
            Quantity = quantity,
            UnitPrice = unitPrice,
            LineTotal = unitPrice * quantity
        };
        charge.SetCreated(id, createdById, createdAt);
        return charge;
    }

    public void Update(string description, int quantity, decimal unitPrice, Guid modifiedById, DateTime modifiedAt)
    {
        Validate(LineNumber, description, quantity, unitPrice);

        Description = description.Trim();
        Quantity = quantity;
        UnitPrice = unitPrice;
        LineTotal = unitPrice * quantity;
        SetModified(modifiedById, modifiedAt);
    }

    private static void Validate(int lineNumber, string description, int quantity, decimal unitPrice)
    {
        if (lineNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lineNumber), "Line number must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Charge description is required.", nameof(description));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }

        if (unitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "Unit price cannot be negative.");
        }
    }
}
