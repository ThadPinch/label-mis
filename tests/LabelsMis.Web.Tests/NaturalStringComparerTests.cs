using LabelsMis.Web.Services.Models;

namespace LabelsMis.Web.Tests;

/// <summary>Die locations are shelf/bin labels; the list sorts them by leading number first, then text.</summary>
public class NaturalStringComparerTests
{
    [Fact]
    public void Sorts_LeadingNumbersByValue_ThenText()
    {
        var locations = new[] { "108", "Tempe", "165/188", "11", "166", "165", "2", "165/9", "A12", "a2", null, "108" };

        var sorted = locations.OrderBy(l => l, NaturalStringComparer.Instance).ToList();

        sorted.Should().Equal("2", "11", "108", "108", "165", "165/9", "165/188", "166", "a2", "A12", "Tempe", null);
    }

    [Fact]
    public void Descending_ReversesTheSameOrder()
    {
        var locations = new[] { "11", "108", "165/188", "165" };

        locations.OrderBy(l => l, NaturalStringComparer.Descending)
            .Should().Equal("165/188", "165", "108", "11");
    }

    [Theory]
    [InlineData("11", "108", -1)]
    [InlineData("165/188", "165", 1)]
    [InlineData("165/188", "166", -1)]
    [InlineData("007", "7", 1)]
    [InlineData("shelf 10", "Shelf 9", 1)]
    [InlineData("12", "12", 0)]
    public void Compare_HandlesNumericRunsAndCase(string x, string y, int expectedSign)
    {
        Math.Sign(NaturalStringComparer.Instance.Compare(x, y)).Should().Be(expectedSign);
    }
}
