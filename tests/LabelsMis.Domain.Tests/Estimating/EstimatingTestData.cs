using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Estimating.Models;

namespace LabelsMis.Domain.Tests.Estimating;

internal static class EstimatingTestData
{
    internal static readonly Guid Indigo6800PressId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    internal static readonly Guid BoppStockId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    internal static readonly Guid LaminateOpId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    internal static readonly Guid DieCutOpId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    internal static readonly Guid SlitOpId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    internal static EstimateRequest CreateWorkedExampleRequest(
        IReadOnlyList<int>? quantities = null,
        decimal? customerMarkupPct = null,
        decimal? minimumMarginPct = null,
        IndigoInkSet? inkSet = null,
        decimal? clickRatePer1000 = null,
        bool? whiteInkUsed = null,
        decimal? whiteClickRatePer1000 = null,
        IReadOnlyList<FinishingOperationRequest>? finishingOperations = null,
        decimal? labelAcrossIn = null,
        decimal? pressWebWidthIn = null,
        decimal? pressEdgeMarginIn = null,
        decimal? setupWasteImpressions = null,
        decimal? runningWastePct = null,
        int? maxLabelsAcrossOverride = null,
        LabelOrientation? labelOrientationOverride = LabelOrientation.AsEntered)
    {
        return new EstimateRequest(
            LabelAcrossIn: labelAcrossIn ?? 4.0m,
            LabelAroundIn: 3.0m,
            CornerRadiusIn: 0.125m,
            GutterAcrossIn: 0.0625m,
            GutterAroundIn: 0.0625m,
            BleedIn: 0.0625m,
            PressId: Indigo6800PressId,
            PressWebWidthIn: pressWebWidthIn ?? 13.0m,
            PressEdgeMarginIn: pressEdgeMarginIn ?? 0.25m,
            PressSetupMinutes: 20m,
            PressCostPerHour: 150m,
            PressSpeedFpm: 100m,
            PressClickBased: true,
            InkSet: inkSet ?? IndigoInkSet.CMYK,
            ClickRatePer1000: clickRatePer1000 ?? 35m,
            WhiteInkUsed: whiteInkUsed ?? false,
            WhiteClickRatePer1000: whiteClickRatePer1000 ?? 0m,
            StockId: BoppStockId,
            StockWidthIn: 13.5m,
            StockCostPerMsi: 0.85m,
            FinishingOperations: finishingOperations ?? DefaultFinishingOperations(),
            Quantities: quantities ?? [5000, 10000, 25000],
            SetupWasteImpressions: setupWasteImpressions ?? 30m,
            RunningWastePct: runningWastePct ?? 0.03m,
            CustomerMarkupPct: customerMarkupPct ?? 0.45m,
            MinimumMarginPct: minimumMarginPct ?? 0.25m,
            MaxLabelsAcrossOverride: maxLabelsAcrossOverride,
            LabelOrientationOverride: labelOrientationOverride);
    }

    internal static IReadOnlyList<FinishingOperationRequest> DefaultFinishingOperations() =>
    [
        new(LaminateOpId, 15m, 200m, 90m, "Gloss laminate"),
        new(DieCutOpId, 30m, 250m, 110m, "Rotary die-cut / matrix strip")
    ];

    internal static IReadOnlyList<FinishingOperationRequest> MultipleFinishingOperations() =>
    [
        new(LaminateOpId, 15m, 200m, 90m, "Gloss laminate"),
        new(DieCutOpId, 30m, 250m, 110m, "Rotary die-cut / matrix strip"),
        new(SlitOpId, 10m, 300m, 85m, "Slit to width")
    ];
}
