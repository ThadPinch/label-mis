using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Estimating;
using LabelsMis.Domain.Estimating.Models;

namespace LabelsMis.Domain.Tests.Estimating;

/// <summary>
/// EPM / EPMW pricing, built from Andrew's 2026-10-21 brief: a 16 oz can label (4.938 × 8),
/// 2 across, no finishing, markup override 1.0, priced at 1k / 10k / 50k on the WS6800 as it is
/// configured on live (13.0" web, 98 fpm, 15 min setup, $125/h, frame factor 2, min repeat 10).
/// Every input and expected figure is read back from his raw /estimates/calculate response
/// (calc_epm_vs_cmyk_nofinish_AFTER_ink_records.json).
/// </summary>
public class EstimatingServiceEpmTests
{
    private const decimal ClickRatePer1000 = 11.50m;

    private readonly EstimatingService _sut = new();

    [Fact]
    public void Calculate_ClickRateMissingOnClickPress_ReturnsErrorAndNoBreaks()
    {
        var request = CanLabel(IndigoInkSet.EPM) with { ClickRatePer1000 = 0m, ClickRateMissing = true };

        var result = _sut.Calculate(request);

        result.QuantityBreaks.Should().BeEmpty("the clicks must not be priced at $0");
        result.Errors.Should().ContainSingle()
            .Which.Should().Be("No ink record for EPM — clicks can't be costed. Add one in Setup → Inks.");
    }

    [Fact]
    public void Calculate_ClickRateMissingOnEpmw_NamesTheLineInkSet()
    {
        var request = CanLabel(IndigoInkSet.EPMW, whiteHits: 1) with { ClickRatePer1000 = 0m, ClickRateMissing = true };

        var result = _sut.Calculate(request);

        result.QuantityBreaks.Should().BeEmpty();
        result.Errors.Should().ContainSingle().Which.Should().StartWith("No ink record for EPMW");
    }

    [Fact]
    public void Calculate_ClickRateMissingOnNonClickPress_StillPrices()
    {
        var request = CanLabel(IndigoInkSet.EPM) with
        {
            PressClickBased = false,
            ClickRatePer1000 = 0m,
            ClickRateMissing = true
        };

        var result = _sut.Calculate(request);

        result.Errors.Should().BeEmpty();
        result.QuantityBreaks.Should().HaveCount(3);
    }

    [Fact]
    public void Calculate_LaminateWithoutStock_WarnsButStillPrices()
    {
        var request = CanLabel(IndigoInkSet.CMYK) with
        {
            FinishingOperations =
            [
                new FinishingOperationRequest(Guid.NewGuid(), 15m, 200m, 90m, "Gloss laminate", MaterialExpected: true)
            ]
        };

        var result = _sut.Calculate(request);

        result.Errors.Should().BeEmpty();
        result.QuantityBreaks.Should().HaveCount(3);
        result.Warnings.Should().ContainSingle()
            .Which.Should().Be("Gloss laminate: no stock selected — its material isn't costed.");
    }

    [Fact]
    public void Calculate_LaminateWithStock_NoMaterialWarning()
    {
        var request = CanLabel(IndigoInkSet.CMYK) with
        {
            FinishingOperations =
            [
                new FinishingOperationRequest(
                    Guid.NewGuid(), 15m, 200m, 90m, "Gloss laminate",
                    StockId: Guid.NewGuid(), StockWidthIn: 13m, StockCostPerMsi: 0.13m, StockLabel: "LAM-5000",
                    MaterialExpected: true),
                new FinishingOperationRequest(Guid.NewGuid(), 5m, 150m, 125m, "Die cut")
            ]
        };

        var result = _sut.Calculate(request);

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty("the laminate has its film and the die cut consumes no stock");
    }

    /// <summary>Andrew's JSON, after the EPM/EPMW ink records were added: every cost line and total
    /// at all three quantities on the single 98 fpm press speed (white slows CMYKW and EPMW to 72).</summary>
    [Theory]
    [InlineData(IndigoInkSet.CMYK, 0, 85.02, 476.70, 2217.25)]
    [InlineData(IndigoInkSet.EPM, 0, 81.36, 446.38, 2068.48)]
    [InlineData(IndigoInkSet.CMYKW, 1, 100.68, 606.57, 2854.49)]
    [InlineData(IndigoInkSet.EPMW, 1, 97.02, 576.25, 2705.72)]
    public void Calculate_AndrewsCanLabelOnSingleSpeedPress_MatchesHisCalculateResponse(
        IndigoInkSet inkSet, int whiteHits, double cost1k, double cost10k, double cost50k)
    {
        var result = _sut.Calculate(CanLabel(inkSet, whiteHits));

        result.Errors.Should().BeEmpty();
        result.Imposition!.LabelsPerImpression.Should().Be(8);
        result.QuantityBreaks.Select(b => b.TotalCost).Should()
            .Equal((decimal)cost1k, (decimal)cost10k, (decimal)cost50k);
        result.QuantityBreaks.Select(b => b.Impressions).Should().Equal(159, 1318, 6468);
    }

    [Fact]
    public void Calculate_AndrewsEpmwLine_CostLinesMatchHisBreakdownAt10k()
    {
        var result = _sut.Calculate(CanLabel(IndigoInkSet.EPMW, whiteHits: 1));

        var lines = result.QuantityBreaks.Single(b => b.Quantity == 10000).CostBreakdown
            .Select(i => (i.Description, i.LineCost))
            .ToList();

        lines.Should().Equal(
            ("Indigo EPMW (3 colors × 1318 impressions × 2 frame factor)", 90.94m),
            ("White (1 hit × 2636 click frames)", 30.31m),
            ("White ink (100% coverage × 1 hit)", 36.92m),
            ("Stock consumption", 285.12m),
            ("Press setup labor", 31.25m),
            ("Press run labor (slowed to 72 fpm for White)", 101.71m));
    }

    /// <summary>The can-label request from Andrew's brief. The white runs in his JSON were priced on
    /// a $0.52/MSI stock; the plain runs on B7771 at $0.455/MSI.</summary>
    internal static EstimateRequest CanLabel(IndigoInkSet inkSet, int whiteHits = 0)
    {
        var specials = whiteHits > 0
            ? new List<SpecialInkSpec>
            {
                // HP-IND-6800-W: $11.50/1,000 clicks, 6.5 mL per 1,000 sq in at 100%, $0.0136/mL,
                // 72 fpm at one hit.
                new("White", whiteHits, ClickRatePer1000, 1m, 150m, 11000m, 6.5m, 72m)
            }
            : [];

        return new EstimateRequest(
            LabelAcrossIn: 4.938m,
            LabelAroundIn: 8m,
            CornerRadiusIn: 0.125m,
            GutterAcrossIn: 0.63m,
            GutterAroundIn: 0m,
            BleedIn: 0m,
            PressId: EstimatingTestData.Indigo6800PressId,
            PressWebWidthIn: 13.0m,
            PressMaxImageWidthIn: 12.59m,
            PressMinRepeatIn: 10m,
            PressMaxRepeatIn: 38.58m,
            PressFrameFactor: 2,
            PressMaxImageLengthIn: 38.58m,
            PressEdgeMarginIn: 0.25m,
            PressSetupMinutes: 15m,
            PressCostPerHour: 125m,
            PressSpeedFpm: 98m,
            PressClickBased: true,
            InkSet: inkSet,
            ClickRatePer1000: ClickRatePer1000,
            SpecialInks: specials,
            StockId: EstimatingTestData.BoppStockId,
            StockWidthIn: 13.0m,
            StockCostPerMsi: whiteHits > 0 ? 0.52m : 0.455m,
            FinishingOperations: [],
            Quantities: [1000, 10000, 50000],
            SetupWasteImpressions: 30m,
            RunningWastePct: 0.03m,
            CustomerMarkupPct: 1.0m,
            MinimumMarginPct: 0.25m,
            MaxLabelsAcrossOverride: 2);
    }
}
