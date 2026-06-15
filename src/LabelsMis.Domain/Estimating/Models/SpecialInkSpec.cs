namespace LabelsMis.Domain.Estimating.Models;

/// <summary>
/// A non-process ink layer (white, silver, or a PMS spot) applied on top of the
/// color set. Cost = multi-hit clicks + bottle/coverage-based ink consumption.
/// </summary>
public record SpecialInkSpec(
    string Label,
    int Hits,
    decimal ClickRatePer1000,
    decimal CoveragePct,
    decimal BottleCost,
    decimal BottleSizeMl,
    decimal MlPer1000SqIn);
