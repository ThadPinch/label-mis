using LabelsMis.Domain.Common;

namespace LabelsMis.Domain.Entities;

/// <summary>A flat, non-label charge quoted on an estimate (die creation, design time, plates).
/// Charges carry through to the sales order and invoice but never become production jobs.</summary>
public class EstimateCharge : EntityBase
{
    private EstimateCharge()
    {
    }

    public Guid EstimateId { get; private set; }
    public Estimate Estimate { get; private set; } = null!;
    public int LineNumber { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal LineTotal { get; private set; }

    public static EstimateCharge Create(
        Guid id,
        Guid estimateId,
        int lineNumber,
        string description,
        int quantity,
        decimal unitPrice,
        Guid createdById,
        DateTime createdAt)
    {
        Validate(lineNumber, description, quantity, unitPrice);

        var charge = new EstimateCharge
        {
            EstimateId = estimateId,
            LineNumber = lineNumber,
            Description = description.Trim(),
            Quantity = quantity,
            UnitPrice = unitPrice,
            LineTotal = unitPrice * quantity
        };
        charge.SetCreated(id, createdById, createdAt);
        return charge;
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
