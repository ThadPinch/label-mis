namespace LabelsMis.Domain.Estimating.Models;

public record QuantityBreakResult(
    int Quantity,
    int Impressions,
    decimal WebLengthFt,
    decimal RunTimeMinutes,
    decimal TotalCost,
    decimal TotalPrice,
    decimal UnitPrice,
    decimal PricePerThousand,
    decimal MarginPct,
    bool BelowMinimumMargin,
    decimal MarkupPctUsed,
    IReadOnlyList<EstimateLineItem> CostBreakdown);
