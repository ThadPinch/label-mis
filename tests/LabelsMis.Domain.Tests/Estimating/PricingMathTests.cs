using LabelsMis.Domain.Estimating;

namespace LabelsMis.Domain.Tests.Estimating;

public class PricingMathTests
{
    private const decimal Tight = 0.0000001m;

    [Theory]
    [InlineData(0.45, 0.3103448)]
    [InlineData(0.55, 0.3548387)]
    [InlineData(1.0, 0.5)]
    [InlineData(0, 0)]
    public void MarkupToMargin_KnownMarkups_ReturnsMarginOnPrice(decimal markup, decimal expectedMargin)
    {
        PricingMath.MarkupToMargin(markup).Should().BeApproximately(expectedMargin, Tight);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(-1.5)]
    public void MarkupToMargin_MarkupAtOrBelowMinusOne_Throws(decimal markup)
    {
        var act = () => PricingMath.MarkupToMargin(markup);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0.3103448, 0.45)]
    [InlineData(0.3548387, 0.55)]
    [InlineData(0.5, 1.0)]
    [InlineData(0, 0)]
    public void MarginToMarkup_KnownMargins_ReturnsMarkupOnCost(decimal margin, decimal expectedMarkup)
    {
        PricingMath.MarginToMarkup(margin).Should().BeApproximately(expectedMarkup, 0.000001m);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(100)]
    public void MarginToMarkup_MarginAtOrAboveOne_Throws(decimal margin)
    {
        var act = () => PricingMath.MarginToMarkup(margin);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0.45)]
    [InlineData(0.55)]
    [InlineData(0.2)]
    [InlineData(1.0)]
    [InlineData(9.5)]
    public void MarginToMarkup_AfterMarkupToMargin_RoundTripsToOriginalMarkup(decimal markup)
    {
        var roundTripped = PricingMath.MarginToMarkup(PricingMath.MarkupToMargin(markup));

        roundTripped.Should().BeApproximately(markup, 0.000000001m);
    }

    [Theory]
    [InlineData(0.45, 31.0)]
    [InlineData(0.55, 35.5)]
    [InlineData(0.3, 23.1)]
    [InlineData(1.0, 50.0)]
    [InlineData(0, 0)]
    public void MarkupToMarginPercent_KnownMarkups_RoundsToOneDecimalPercent(decimal markup, decimal expectedPercent)
    {
        PricingMath.MarkupToMarginPercent(markup).Should().Be(expectedPercent);
    }

    [Theory]
    [InlineData(50, 1.0)]
    [InlineData(31.0, 0.4492754)]
    [InlineData(0, 0)]
    public void MarginPercentToMarkup_WithoutCurrentMarkup_ConvertsEnteredPercent(decimal marginPercent, decimal expectedMarkup)
    {
        PricingMath.MarginPercentToMarkup(marginPercent).Should().BeApproximately(expectedMarkup, Tight);
    }

    [Theory]
    [InlineData(0.45)]
    [InlineData(0.55)]
    [InlineData(0.3333)]
    [InlineData(1.2345)]
    [InlineData(0.0001)]
    [InlineData(9.98)]
    public void MarginPercentToMarkup_DisplayedMarginReentered_ReturnsStoredMarkupExactly(decimal storedMarkup)
    {
        var displayed = PricingMath.MarkupToMarginPercent(storedMarkup);

        PricingMath.MarginPercentToMarkup(displayed, storedMarkup).Should().Be(storedMarkup);
    }

    [Fact]
    public void MarginPercentToMarkup_ChangedMargin_ReturnsConvertedEnteredValue()
    {
        // Stored 45% markup shows as 31.0%; the user types 35.5% (a 55% markup).
        var result = PricingMath.MarginPercentToMarkup(35.5m, 0.45m);

        result.Should().BeApproximately(0.5503876m, Tight);
    }

    [Fact]
    public void MarginPercentToMarkup_MarginOneDisplayStepAway_ReturnsConvertedEnteredValue()
    {
        // 0.45 displays as 31.0%; 31.1% is outside half a display step, so it is a real edit.
        var result = PricingMath.MarginPercentToMarkup(31.1m, 0.45m);

        result.Should().NotBe(0.45m).And.BeApproximately(0.4513788m, Tight);
    }

    [Fact]
    public void MarginPercentToMarkup_NullCurrentMarkup_ConvertsEnteredPercent()
    {
        PricingMath.MarginPercentToMarkup(31.0m, null).Should().BeApproximately(0.4492754m, Tight);
    }

    [Fact]
    public void MarginPercentToMarkup_EnteredPercentAtOrAboveHundred_Throws()
    {
        var act = () => PricingMath.MarginPercentToMarkup(100m, 0.45m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
