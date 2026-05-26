using LabelsMis.Domain.Common;

namespace LabelsMis.Domain.Entities;

public class EstimateQuantityBreak : EntityBase
{
    private EstimateQuantityBreak()
    {
    }

    public Guid EstimateLineId { get; private set; }
    public EstimateLine EstimateLine { get; private set; } = null!;
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal TotalPrice { get; private set; }
    public decimal CalculatedCost { get; private set; }
    public decimal MarginPct { get; private set; }
    public string CostBreakdownJson { get; private set; } = "[]";

    public static EstimateQuantityBreak Create(
        Guid id,
        Guid estimateLineId,
        int quantity,
        decimal unitPrice,
        decimal totalPrice,
        decimal calculatedCost,
        decimal marginPct,
        string costBreakdownJson,
        Guid createdById,
        DateTime createdAt)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }

        var breakRow = new EstimateQuantityBreak
        {
            EstimateLineId = estimateLineId,
            Quantity = quantity,
            UnitPrice = unitPrice,
            TotalPrice = totalPrice,
            CalculatedCost = calculatedCost,
            MarginPct = marginPct,
            CostBreakdownJson = string.IsNullOrWhiteSpace(costBreakdownJson) ? "[]" : costBreakdownJson
        };
        breakRow.SetCreated(id, createdById, createdAt);
        return breakRow;
    }
}
