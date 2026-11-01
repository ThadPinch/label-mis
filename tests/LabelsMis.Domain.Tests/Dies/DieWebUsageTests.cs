using System.Globalization;
using LabelsMis.Domain.Dies;

namespace LabelsMis.Domain.Tests.Dies;

public class DieWebUsageTests
{
    [Fact]
    public void Usage_IsLabelsAcrossTimesWidthOverWeb()
    {
        DieWebUsage.Usage(3m, 4, 13m).Should().Be(12m / 13m);
        DieWebUsage.Usage(3m, 4, 0m).Should().BeNull();
        DieWebUsage.Usage(0m, 4, 13m).Should().BeNull();
        DieWebUsage.Usage(3m, 0, 13m).Should().BeNull();
    }

    [Fact]
    public void Warning_BelowSixtyPercent_SaysWhatToCheck()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var warning = DieWebUsage.Warning(2.5m, 3, 13m);

            warning.Should().NotBeNull();
            warning.Should().Contain("3 across × 2.5\" = 7.5\" uses only 58").And.Contain("sideways");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(2.6, 3, 13)]   // 60% exactly: no warning
    [InlineData(8, 1, 13)]     // the 12 oz example: 61.5%, above the rule
    [InlineData(3, 4, 13)]
    public void Warning_AtOrAboveSixtyPercent_IsNull(double width, int across, double web)
    {
        DieWebUsage.Warning((decimal)width, across, (decimal)web).Should().BeNull();
    }
}
