using LabelsMis.Domain.Search;

namespace LabelsMis.Domain.Tests.Search;

public class SearchRankerTests
{
    [Theory]
    [InlineData("ACME FOODS", "ACME FOODS", SearchMatch.Exact)]
    [InlineData("Acme Foods", "ACME", SearchMatch.Prefix)]
    [InlineData("Big Acme", "ACME", SearchMatch.WordStart)]
    [InlineData("Blueberry berry", "BERRY", SearchMatch.WordStart)]
    [InlineData("Blueberry", "BERRY", SearchMatch.Contains)]
    [InlineData("Acme", "ZETA", SearchMatch.None)]
    [InlineData(null, "ACME", SearchMatch.None)]
    public void Match_ClassifiesHowTheValueMatches(string? value, string term, SearchMatch expected)
    {
        SearchRanker.Match(value, term).Should().Be(expected);
    }

    [Fact]
    public void Score_ExactDocumentNumber_IsTheExactScore()
    {
        var query = SearchQuery.Parse("JOB-2026-00052");

        SearchRanker.Score(query, "JOB-2026-00052").Should().Be(SearchRanker.ExactScore);
    }

    [Fact]
    public void Score_BareNumber_RanksTheMatchingSequenceAboveOtherNumbersContainingIt()
    {
        var query = SearchQuery.Parse("52");

        var exactSequence = SearchRanker.Score(query, "JOB-2026-00052");
        var containsDigits = SearchRanker.Score(query, "JOB-2026-00152");

        exactSequence.Should().Be(SearchRanker.NumberSuffixScore);
        containsDigits.Should().BeLessThan(exactSequence);
    }

    [Fact]
    public void Score_CustomerName_RanksTheCustomerAboveItsOrders()
    {
        var query = SearchQuery.Parse("acme");

        var customer = SearchRanker.Score(query, "ACM", name: "Acme Foods");
        var order = SearchRanker.Score(query, "SO-2026-00052", related: ["Acme Foods"]);

        customer.Should().BeGreaterThan(order);
    }

    [Fact]
    public void Score_DetailMatch_BeatsARelatedMatch()
    {
        var query = SearchQuery.Parse("4471");

        var poOnOrder = SearchRanker.Score(query, "SO-2026-00052", details: ["4471"]);
        var customerNamedAfterIt = SearchRanker.Score(query, "SO-2026-00053", related: ["4471 Brewing"]);

        poOnOrder.Should().BeGreaterThan(customerNamedAfterIt);
    }

    [Fact]
    public void Score_SeveralTerms_IsSetByTheWeakestTerm()
    {
        var query = SearchQuery.Parse("acme cherry");

        var both = SearchRanker.Score(query, "JOB-1", name: "Cherry pie label", related: ["Acme Foods"]);
        var onlyOne = SearchRanker.Score(query, "JOB-2", name: "Cherry pie label", related: ["Zeta Co"]);

        both.Should().BeGreaterThan(0);
        onlyOne.Should().Be(0);
    }

    [Fact]
    public void Score_EmptyQuery_IsZero()
    {
        SearchRanker.Score(SearchQuery.Empty, "JOB-2026-00052").Should().Be(0);
    }
}
