using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.ValueObjects;

/// <summary>
/// A point-in-time snapshot of the physical description of a label — everything that says what the
/// label <em>is</em> (dimensions, material, color, finishing, waste, layout, artwork), independent of
/// the commercial/planning wrapper it lives on (an estimate line, an order line, or a job).
///
/// The spec is copied forward at each boundary (estimate → order → job), so each holder keeps its own
/// impression and can diverge from the one before it. See <c>docs/labelspec-refactor.md</c>.
/// </summary>
public record LabelSpec(
    decimal LabelAcrossIn,
    decimal LabelAroundIn,
    decimal CornerRadiusIn,
    decimal GutterAcrossIn,
    decimal GutterAroundIn,
    decimal BleedIn,
    Guid SubstrateId,
    Guid? DieId,
    InkSet InkSet,
    int WhiteHits,
    decimal WhiteCoveragePct,
    string SpotsJson,
    string FinishingOperationsJson,
    decimal SetupWasteImpressions,
    decimal RunningWastePct,
    int? MaxLabelsAcrossOverride,
    LabelOrientation? LabelOrientationOverride,
    string? ArtworkFilePath,
    UnwindDirection? Unwind = null)
{
    /// <summary>Builds a normalized spec, defaulting the JSON columns to an empty array.</summary>
    public static LabelSpec Create(
        decimal labelAcrossIn,
        decimal labelAroundIn,
        decimal cornerRadiusIn,
        decimal gutterAcrossIn,
        decimal gutterAroundIn,
        decimal bleedIn,
        Guid substrateId,
        Guid? dieId,
        InkSet inkSet,
        int whiteHits,
        decimal whiteCoveragePct,
        string? spotsJson,
        string? finishingOperationsJson,
        decimal setupWasteImpressions,
        decimal runningWastePct,
        int? maxLabelsAcrossOverride,
        LabelOrientation? labelOrientationOverride,
        string? artworkFilePath,
        UnwindDirection? unwind = null) => new(
            labelAcrossIn,
            labelAroundIn,
            cornerRadiusIn,
            gutterAcrossIn,
            gutterAroundIn,
            bleedIn,
            substrateId,
            dieId,
            inkSet,
            Math.Max(0, whiteHits),
            whiteCoveragePct <= 0 ? 1m : whiteCoveragePct,
            NormalizeJson(spotsJson),
            NormalizeJson(finishingOperationsJson),
            setupWasteImpressions,
            runningWastePct,
            maxLabelsAcrossOverride,
            labelOrientationOverride,
            string.IsNullOrWhiteSpace(artworkFilePath) ? null : artworkFilePath.Trim(),
            unwind);

    private static string NormalizeJson(string? json) =>
        string.IsNullOrWhiteSpace(json) ? "[]" : json;
}
