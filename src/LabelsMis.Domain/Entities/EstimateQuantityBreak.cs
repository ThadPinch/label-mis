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

    /// <summary>The quoted price. Equals the calculated price unless the line is outsourced, in which
    /// case it is the final price entered against the vendor cost.</summary>
    public decimal UnitPrice { get; private set; }
    public decimal TotalPrice { get; private set; }

    /// <summary>What the in-house calculation produced — kept even when outsourced so the two can be compared.</summary>
    public decimal CalculatedCost { get; private set; }
    public decimal CalculatedUnitPrice { get; private set; }
    public decimal CalculatedTotalPrice { get; private set; }

    /// <summary>Margin on the quoted price against the relevant cost (vendor cost when outsourced).</summary>
    public decimal MarginPct { get; private set; }

    /// <summary>Vendor's cost for this quantity when the line is outsourced; null otherwise.</summary>
    public decimal? OutsourceCost { get; private set; }
    public bool IsOutsourced => OutsourceCost.HasValue;

    /// <summary>Markup override applied to this quantity only; null means the line's
    /// markup (or the customer default) was used.</summary>
    public decimal? MarkupPctOverride { get; private set; }

    public string CostBreakdownJson { get; private set; } = "[]";

    public static EstimateQuantityBreak Create(
        Guid id,
        Guid estimateLineId,
        int quantity,
        decimal unitPrice,
        decimal totalPrice,
        decimal calculatedCost,
        decimal marginPct,
        decimal? markupPctOverride,
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
            CalculatedUnitPrice = unitPrice,
            CalculatedTotalPrice = totalPrice,
            MarginPct = marginPct,
            MarkupPctOverride = markupPctOverride,
            CostBreakdownJson = string.IsNullOrWhiteSpace(costBreakdownJson) ? "[]" : costBreakdownJson
        };
        breakRow.SetCreated(id, createdById, createdAt);
        return breakRow;
    }

    /// <summary>
    /// Replaces the quoted price with an outsourced final price: <see cref="UnitPrice"/>/<see cref="TotalPrice"/>
    /// become the entered price, <see cref="MarginPct"/> is measured against the vendor cost, and the
    /// calculated in-house cost/price stay on the row for comparison.
    /// </summary>
    public void ApplyOutsourcePricing(decimal vendorCost, decimal finalTotalPrice)
    {
        if (vendorCost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(vendorCost), "Vendor cost cannot be negative.");
        }

        if (finalTotalPrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(finalTotalPrice), "Final price cannot be negative.");
        }

        OutsourceCost = vendorCost;
        TotalPrice = finalTotalPrice;
        UnitPrice = finalTotalPrice / Quantity;
        MarginPct = finalTotalPrice == 0m ? 0m : (finalTotalPrice - vendorCost) / finalTotalPrice;
    }
}
