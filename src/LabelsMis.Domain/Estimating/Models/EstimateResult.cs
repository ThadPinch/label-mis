namespace LabelsMis.Domain.Estimating.Models;

public record EstimateResult(
    ImpositionResult? Imposition,
    IReadOnlyList<QuantityBreakResult> QuantityBreaks,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);
