namespace LabelsMis.Domain.Estimating.Models;

public record ImpositionResult(
    int LabelsAcross,
    int LabelsAround,
    int LabelsPerImpression,
    decimal RepeatLengthIn,
    decimal UtilizationPct);
