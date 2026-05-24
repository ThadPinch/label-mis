using LabelsMis.Domain.Estimating;
using LabelsMis.Domain.Estimating.Models;

namespace LabelsMis.Domain.Tests.Estimating;

public class EstimatingServiceTests
{
    private readonly EstimatingService _sut = new();

    [Fact]
    public void Calculate_WorkedExample_ProducesExpectedResultsForAllQuantities()
    {
        var request = EstimatingTestData.CreateWorkedExampleRequest();
        var result = _sut.Calculate(request);

        result.Errors.Should().BeEmpty();
        result.Imposition.Should().NotBeNull();
        result.Imposition!.LabelsAcross.Should().Be(3);
        result.Imposition.LabelsAround.Should().Be(1);
        result.Imposition.LabelsPerImpression.Should().Be(3);
        result.Imposition.RepeatLengthIn.Should().Be(3.0625m);
        result.Imposition.UtilizationPct.Should().BeApproximately(0.9231m, 0.0001m);

        result.QuantityBreaks.Should().HaveCount(3);

        var break25k = result.QuantityBreaks.Single(q => q.Quantity == 25000);
        break25k.Impressions.Should().Be(8614);
        break25k.TotalCost.Should().Be(819.32m);
        break25k.TotalPrice.Should().Be(1188.01m);
        break25k.UnitPrice.Should().Be(0.0475m);
        break25k.PricePerThousand.Should().Be(47.52m);
        break25k.MarginPct.Should().BeApproximately(0.31m, 0.001m);
        break25k.BelowMinimumMargin.Should().BeFalse();
        break25k.CostBreakdown.Should().Contain(item =>
            item.Category == "Press click" && item.LineCost == 301.49m);
        break25k.CostBreakdown.Should().Contain(item =>
            item.Category == "Substrate" && item.LineCost == 302.77m);
        break25k.CostBreakdown.Should().Contain(item =>
            item.Category == "Press setup" && item.LineCost == 50.00m);
        break25k.CostBreakdown.Should().Contain(item =>
            item.Category == "Press run" && item.LineCost == 54.95m);
        break25k.CostBreakdown.Should().Contain(item =>
            item.Description == "Gloss laminate" && item.LineCost == 38.99m);
        break25k.CostBreakdown.Should().Contain(item =>
            item.Description == "Rotary die-cut / matrix strip" && item.LineCost == 71.12m);

        var break5k = result.QuantityBreaks.Single(q => q.Quantity == 5000);
        break5k.Impressions.Should().Be(1747);
        break5k.TotalCost.Should().Be(267.87m);
        break5k.TotalPrice.Should().Be(388.41m);

        var break10k = result.QuantityBreaks.Single(q => q.Quantity == 10000);
        break10k.Impressions.Should().Be(3464);
        break10k.TotalCost.Should().Be(405.77m);
        break10k.TotalPrice.Should().Be(588.37m);
    }

    [Fact]
    public void Calculate_EpmMode_ProducesLowerClickCostThanCmyk()
    {
        var cmykRequest = EstimatingTestData.CreateWorkedExampleRequest(
            quantities: [25000],
            inkSet: IndigoInkSet.CMYK,
            clickRatePer1000: 35m);

        var epmRequest = EstimatingTestData.CreateWorkedExampleRequest(
            quantities: [25000],
            inkSet: IndigoInkSet.EPM,
            clickRatePer1000: 28m);

        var cmykResult = _sut.Calculate(cmykRequest);
        var epmResult = _sut.Calculate(epmRequest);

        var cmykClick = cmykResult.QuantityBreaks[0].CostBreakdown
            .Single(item => item.Description.Contains("click charge", StringComparison.Ordinal));
        var epmClick = epmResult.QuantityBreaks[0].CostBreakdown
            .Single(item => item.Description.Contains("click charge", StringComparison.Ordinal));

        epmClick.LineCost.Should().BeLessThan(cmykClick.LineCost);
        epmResult.QuantityBreaks[0].TotalCost.Should().BeLessThan(cmykResult.QuantityBreaks[0].TotalCost);
    }

    [Fact]
    public void Calculate_CmykwWithWhiteInk_AddsSeparateWhiteClickLine()
    {
        var request = EstimatingTestData.CreateWorkedExampleRequest(
            quantities: [25000],
            inkSet: IndigoInkSet.CMYKW,
            whiteInkUsed: true,
            whiteClickRatePer1000: 12m);

        var result = _sut.Calculate(request);

        result.Errors.Should().BeEmpty();
        result.QuantityBreaks[0].CostBreakdown.Should().Contain(item =>
            item.Description == "White ink click charge" && item.LineCost == 103.37m);
    }

    [Fact]
    public void Calculate_LabelTooWide_ReturnsErrorAndNoQuantityBreaks()
    {
        var request = EstimatingTestData.CreateWorkedExampleRequest(
            quantities: [25000],
            labelAcrossIn: 13.0m,
            pressWebWidthIn: 13.0m,
            pressEdgeMarginIn: 0.25m);

        var result = _sut.Calculate(request);

        result.Errors.Should().Contain("Label too wide for press");
        result.QuantityBreaks.Should().BeEmpty();
        result.Imposition.Should().BeNull();
    }

    [Fact]
    public void Calculate_LowUtilization_EmitsWarningAndStillCalculates()
    {
        var request = EstimatingTestData.CreateWorkedExampleRequest(
            quantities: [25000],
            labelAcrossIn: 6.4375m);

        var result = _sut.Calculate(request);

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().Contain("Low web utilization, consider gang or different press");
        result.QuantityBreaks.Should().HaveCount(1);
        result.Imposition!.LabelsAcross.Should().Be(1);
    }

    [Fact]
    public void Calculate_NoFinishing_ProducesZeroFinishingCost()
    {
        var request = EstimatingTestData.CreateWorkedExampleRequest(
            quantities: [25000],
            finishingOperations: []);

        var result = _sut.Calculate(request);

        result.Errors.Should().BeEmpty();
        result.QuantityBreaks[0].CostBreakdown.Should().NotContain(item => item.Category == "Finishing");
        result.QuantityBreaks[0].TotalCost.Should().Be(709.21m);
    }

    [Fact]
    public void Calculate_MultipleFinishingOps_EachAppearsInBreakdown()
    {
        var request = EstimatingTestData.CreateWorkedExampleRequest(
            quantities: [25000],
            finishingOperations: EstimatingTestData.MultipleFinishingOperations());

        var result = _sut.Calculate(request);

        var breakdown = result.QuantityBreaks[0].CostBreakdown;
        breakdown.Should().Contain(item => item.Description == "Gloss laminate");
        breakdown.Should().Contain(item => item.Description == "Rotary die-cut / matrix strip");
        breakdown.Should().Contain(item => item.Description == "Slit to width");
        result.QuantityBreaks[0].TotalCost.Should().Be(843.87m);
    }

    [Fact]
    public void Calculate_BelowMinimumMargin_FlagsResult()
    {
        var request = EstimatingTestData.CreateWorkedExampleRequest(
            quantities: [25000],
            customerMarkupPct: 0.05m,
            minimumMarginPct: 0.10m);

        var result = _sut.Calculate(request);

        result.QuantityBreaks[0].BelowMinimumMargin.Should().BeTrue();
        result.QuantityBreaks[0].MarginPct.Should().BeApproximately(0.0476m, 0.0001m);
    }

    [Fact]
    public void Calculate_QuantityBreaks_UnitPriceDecreasesAsQuantityIncreases()
    {
        var request = EstimatingTestData.CreateWorkedExampleRequest();
        var result = _sut.Calculate(request);

        var unitPrices = result.QuantityBreaks
            .OrderBy(q => q.Quantity)
            .Select(q => q.UnitPrice)
            .ToList();

        unitPrices[1].Should().BeLessThanOrEqualTo(unitPrices[0]);
        unitPrices[2].Should().BeLessThanOrEqualTo(unitPrices[1]);
    }

    [Fact]
    public void Calculate_SmallQuantity_SetupWasteDominatesPerUnitCost()
    {
        var smallRequest = EstimatingTestData.CreateWorkedExampleRequest(
            quantities: [100],
            setupWasteImpressions: 100m);

        var largeRequest = EstimatingTestData.CreateWorkedExampleRequest(
            quantities: [100000],
            setupWasteImpressions: 100m);

        var smallResult = _sut.Calculate(smallRequest).QuantityBreaks[0];
        var largeResult = _sut.Calculate(largeRequest).QuantityBreaks[0];

        var smallSetupShare = smallResult.Impressions - (int)Math.Ceiling(100 * 1.03m / 3);
        var largeSetupShare = largeResult.Impressions - (int)Math.Ceiling(100000 * 1.03m / 3);

        smallSetupShare.Should().BeGreaterThan(0);
        largeSetupShare.Should().Be(100);
        smallResult.UnitPrice.Should().BeGreaterThan(largeResult.UnitPrice * 10);
    }

    [Fact]
    public void Calculate_SimilarQuantities_ProduceSimilarPricing()
    {
        var request25k = EstimatingTestData.CreateWorkedExampleRequest(quantities: [25000]);
        var request25001 = EstimatingTestData.CreateWorkedExampleRequest(quantities: [25001]);

        var result25k = _sut.Calculate(request25k).QuantityBreaks[0];
        var result25001 = _sut.Calculate(request25001).QuantityBreaks[0];

        var priceDeltaPct = Math.Abs(result25001.UnitPrice - result25k.UnitPrice) / result25k.UnitPrice;
        priceDeltaPct.Should().BeLessThan(0.001m);
    }

    [Fact(Skip = "awaiting historical job data")]
    public void Calculate_HistoricalJobs_WithinTwoPercentOfQuotedPrices()
    {
        true.Should().BeTrue();
    }
}
