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

    /// <summary>The acceptance table in the EPM plan: with the WS6800's separation speeds
    /// (131 / 98 / 72) the 10k cost totals are CMYK $476.70, EPM $427.57, CMYKW + 1 white $606.57
    /// and EPMW + 1 white $549.25. Andrew printed .58 / .26 because he rounded the click line up;
    /// his own JSON's click lines ($90.94) give these.</summary>
    [Theory]
    [InlineData(IndigoInkSet.CMYK, 0, 476.70, "Press run labor (4 separations at 98 fpm)", 74.71)]
    [InlineData(IndigoInkSet.EPM, 0, 427.57, "Press run labor (3 separations at 131 fpm)", 55.90)]
    [InlineData(IndigoInkSet.CMYKW, 1, 606.57, "Press run labor (5 separations at 72 fpm)", 101.71)]
    [InlineData(IndigoInkSet.EPMW, 1, 549.25, "Press run labor (4 separations at 98 fpm)", 74.71)]
    public void Calculate_AndrewsCanLabelOnSeparationSpeedPress_MatchesTheAcceptanceTotals(
        IndigoInkSet inkSet, int whiteHits, double totalCost10k, string runDescription, double runCost10k)
    {
        var result = _sut.Calculate(WithWs6800SeparationSpeeds(CanLabel(inkSet, whiteHits)));

        result.Errors.Should().BeEmpty();
        var break10k = result.QuantityBreaks.Single(b => b.Quantity == 10000);
        break10k.TotalCost.Should().Be((decimal)totalCost10k);
        break10k.CostBreakdown.Single(i => i.Category == "Press run")
            .Should().Match<EstimateLineItem>(i => i.Description == runDescription && i.LineCost == (decimal)runCost10k);
    }

    [Fact]
    public void Calculate_EpmOnSeparationSpeedPress_IsAboutTenPercentBelowCmyk()
    {
        var cmyk = _sut.Calculate(WithWs6800SeparationSpeeds(CanLabel(IndigoInkSet.CMYK))).QuantityBreaks[1].TotalCost;
        var epm = _sut.Calculate(WithWs6800SeparationSpeeds(CanLabel(IndigoInkSet.EPM))).QuantityBreaks[1].TotalCost;

        (1m - epm / cmyk).Should().BeApproximately(0.103m, 0.001m, "Andrew's brief: EPM saves 10.3% at 10k");
    }

    /// <summary>A press with no separation speeds prices exactly as before: same totals, same
    /// run lines (white still slows CMYKW/EPMW to 72) — the whole result is identical.</summary>
    [Theory]
    [InlineData(IndigoInkSet.CMYK, 0)]
    [InlineData(IndigoInkSet.EPM, 0)]
    [InlineData(IndigoInkSet.CMYKW, 1)]
    [InlineData(IndigoInkSet.EPMW, 1)]
    public void Calculate_PressWithoutSeparationSpeeds_BehavesExactlyAsBefore(IndigoInkSet inkSet, int whiteHits)
    {
        var request = CanLabel(inkSet, whiteHits);
        request.PressSpeedFpm3Separations.Should().BeNull();

        var result = _sut.Calculate(request);
        var whiteSlowedTo72 = whiteHits > 0;

        result.QuantityBreaks.Select(b => b.CostBreakdown.Single(i => i.Category == "Press run").Description)
            .Should().AllBe(whiteSlowedTo72 ? "Press run labor (slowed to 72 fpm for White)" : "Press run labor");
        result.QuantityBreaks.Select(b => b.RunTimeMinutes).Should().Equal(
            whiteSlowedTo72 ? new[] { 20.89m, 63.82m, 254.56m } : new[] { 19.33m, 50.86m, 191.0m });
    }

    [Fact]
    public void Calculate_OnlySomeSeparationSpeedsSet_UnsetCountFallsBackToPressSpeedAndWhiteSlowdown()
    {
        // Only the 3-separation speed is configured: EPM uses it, CMYKW + 1 white (5) does not.
        var partial = (EstimateRequest r) => r with { PressSpeedFpm3Separations = 131m };

        var epm = _sut.Calculate(partial(CanLabel(IndigoInkSet.EPM)));
        var cmykw = _sut.Calculate(partial(CanLabel(IndigoInkSet.CMYKW, whiteHits: 1)));

        epm.QuantityBreaks[1].TotalCost.Should().Be(427.57m);
        cmykw.QuantityBreaks[1].TotalCost.Should().Be(606.57m);
        cmykw.QuantityBreaks[1].CostBreakdown.Should().Contain(i =>
            i.Description == "Press run labor (slowed to 72 fpm for White)");
    }

    [Fact]
    public void Calculate_SeparationsCountWhiteAndSpotHits_AndCapAtTheFivePlusSpeed()
    {
        // EPMW with two white hits = 5 separations; CMYK + one spot hit = 5; CMYK + spot × 2 = 6 → 5+.
        var twoWhites = _sut.Calculate(WithWs6800SeparationSpeeds(CanLabel(IndigoInkSet.EPMW, whiteHits: 2)));
        var oneSpot = _sut.Calculate(WithWs6800SeparationSpeeds(CanLabel(IndigoInkSet.CMYK_PlusSpot) with
        {
            SpecialInks = [new SpecialInkSpec("Silver", 1, ClickRatePer1000, 1m, 0m, 0m, 0m)]
        }));
        var twoSpotHits = _sut.Calculate(WithWs6800SeparationSpeeds(CanLabel(IndigoInkSet.CMYK_PlusSpot) with
        {
            SpecialInks = [new SpecialInkSpec("Silver", 2, ClickRatePer1000, 1m, 0m, 0m, 0m)]
        }));

        RunLine(twoWhites).Should().Be("Press run labor (5 separations at 72 fpm)");
        RunLine(oneSpot).Should().Be("Press run labor (5 separations at 72 fpm)");
        RunLine(twoSpotHits).Should().Be("Press run labor (6 separations at 72 fpm)");
    }

    [Fact]
    public void Calculate_SpotSlowerThanSeparationSpeed_StillSlowsTheRun()
    {
        // Only the white's slowdown is replaced by the count; a spot that is slower still governs.
        var request = WithWs6800SeparationSpeeds(CanLabel(IndigoInkSet.CMYK_PlusSpot)) with
        {
            SpecialInks = [new SpecialInkSpec("Silver", 1, ClickRatePer1000, 1m, 0m, 0m, 0m, SpeedFpmOverride: 50m)]
        };

        RunLine(_sut.Calculate(request)).Should().Be("Press run labor (slowed to 50 fpm for Silver)");
    }

    private static string RunLine(EstimateResult result) =>
        result.QuantityBreaks[1].CostBreakdown.Single(i => i.Category == "Press run").Description;

    /// <summary>The WS6800's separation speeds as the migration backfills them on live.</summary>
    internal static EstimateRequest WithWs6800SeparationSpeeds(EstimateRequest request) => request with
    {
        PressSpeedFpm3Separations = 131m,
        PressSpeedFpm4Separations = 98m,
        PressSpeedFpm5PlusSeparations = 72m
    };

    /// <summary>The can-label request from Andrew's brief. The white runs in his JSON were priced on
    /// a $0.52/MSI stock; the plain runs on B7771 at $0.455/MSI.</summary>
    internal static EstimateRequest CanLabel(IndigoInkSet inkSet, int whiteHits = 0)
    {
        var specials = whiteHits > 0
            ? new List<SpecialInkSpec>
            {
                // HP-IND-6800-W: $11.50/1,000 clicks, 6.5 mL per 1,000 sq in at 100%, $0.0136/mL,
                // 72 fpm at one hit.
                new("White", whiteHits, ClickRatePer1000, 1m, 150m, 11000m, 6.5m, 72m, IsWhite: true)
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
