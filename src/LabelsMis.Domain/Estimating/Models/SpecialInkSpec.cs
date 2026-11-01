namespace LabelsMis.Domain.Estimating.Models;

/// <summary>
/// A non-process ink layer (white, silver, or a PMS spot) applied on top of the
/// color set. Cost = multi-hit clicks + bottle/coverage-based ink consumption.
/// <paramref name="SpeedFpmOverride"/> is the absolute press speed (fpm) this ink
/// forces at the selected hit count; null means no slowdown from this ink.
/// <paramref name="IsWhite"/> marks the ink set's white: on a press with per-separation
/// speeds its hits are counted as separations, so its own slowdown is not applied.
/// </summary>
public record SpecialInkSpec(
    string Label,
    int Hits,
    decimal ClickRatePer1000,
    decimal CoveragePct,
    decimal BottleCost,
    decimal BottleSizeMl,
    decimal MlPer1000SqIn,
    decimal? SpeedFpmOverride = null,
    bool IsWhite = false);
