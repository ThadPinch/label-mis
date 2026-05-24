namespace LabelsMis.Domain.Estimating.Models;

public record EstimateLineItem(
    string Category,
    string Description,
    decimal Quantity,
    string Unit,
    decimal UnitCost,
    decimal LineCost);
