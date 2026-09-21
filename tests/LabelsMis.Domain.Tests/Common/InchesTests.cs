using LabelsMis.Domain.Common;

namespace LabelsMis.Domain.Tests.Common;

public class InchesTests
{
    [Theory]
    [InlineData(1, 25.4)]
    [InlineData(4, 101.6)]
    [InlineData(3.9375, 100.0)]
    [InlineData(4.125, 104.8)]
    [InlineData(0.0625, 1.6)]
    [InlineData(0, 0)]
    public void ToMm_MultipliesBy25Point4_RoundedToOneDecimal(decimal inches, decimal expectedMm)
    {
        Inches.ToMm(inches).Should().Be(expectedMm);
    }

    [Fact]
    public void ToMm_RoundsMidpointAwayFromZero()
    {
        // 0.75 in = 19.05 mm exactly; banker's rounding would give 19.0.
        Inches.ToMm(0.75m).Should().Be(19.1m);
    }

    [Theory]
    [InlineData(4, "4 in (101.6 mm)")]
    [InlineData(4.125, "4.125 in (104.8 mm)")]
    [InlineData(3.9375, "3.9375 in (100.0 mm)")]
    public void FormatWithMm_ShowsInchesThenMillimetres(decimal inches, string expected)
    {
        Inches.FormatWithMm(inches).Should().Be(expected);
    }

    [Fact]
    public void FormatSizeWithMm_ShowsBothDimensionsInBothUnits()
    {
        Inches.FormatSizeWithMm(4m, 3m).Should().Be("4 × 3 in (101.6 × 76.2 mm)");
    }
}
