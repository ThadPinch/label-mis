using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Estimating.Models;

public record ImpositionResult(
    int LabelsAcross,
    int LabelsAround,
    int LabelsPerImpression,
    int FramesPerImpression,
    decimal FrameRepeatIn,
    decimal LayoutRepeatIn,
    decimal RepeatLengthIn,
    decimal UtilizationPct,
    LabelOrientation Orientation,
    int MaxLabelsAcross,
    decimal EffectiveLabelAcrossIn,
    decimal EffectiveLabelAroundIn);
