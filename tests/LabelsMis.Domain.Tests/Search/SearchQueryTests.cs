using LabelsMis.Domain.Search;

namespace LabelsMis.Domain.Tests.Search;

public class SearchQueryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_Blank_IsEmpty(string? raw)
    {
        SearchQuery.Parse(raw).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Parse_Words_UppercasesCollapsesAndDedupes()
    {
        var query = SearchQuery.Parse("  acme   cherry  ACME ");

        query.Should().Satisfy<SearchQuery>(q =>
        {
            q.Text.Should().Be("acme cherry ACME");
            q.Terms.Should().Equal("ACME", "CHERRY");
            q.Hint.Should().BeNull();
            q.Digits.Should().BeNull();
        });
    }

    [Theory]
    [InlineData("JOB-2026-00052", SearchCategory.Job, "JOB-2026-00052", "00052")]
    [InlineData("job-2026-52", SearchCategory.Job, "JOB-2026-00052", "52")]
    [InlineData("job 52", SearchCategory.Job, "52", "52")]
    [InlineData("SO#52", SearchCategory.SalesOrder, "52", "52")]
    [InlineData("inv37", SearchCategory.Invoice, "37", "37")]
    [InlineData("po 4471", SearchCategory.PurchaseOrder, "4471", "4471")]
    [InlineData("EST-2026-00003", SearchCategory.Estimate, "EST-2026-00003", "00003")]
    [InlineData("shp 35", SearchCategory.Shipment, "35", "35")]
    [InlineData("R-2026-00012", SearchCategory.Roll, "R-2026-00012", "00012")]
    public void Parse_DocumentNumber_SetsHintTermAndDigits(string raw, SearchCategory hint, string term, string digits)
    {
        var query = SearchQuery.Parse(raw);

        query.Should().Satisfy<SearchQuery>(q =>
        {
            q.Hint.Should().Be(hint);
            q.Terms.Should().Equal(term);
            q.Digits.Should().Be(digits);
        });
    }

    [Fact]
    public void Parse_PrefixWithFourDigits_ReadsThemAsTheNumberNotAYear()
    {
        var query = SearchQuery.Parse("JOB-2026");

        query.Terms.Should().Equal("2026");
    }

    [Fact]
    public void Parse_PrefixInsideAWord_IsPlainText()
    {
        var query = SearchQuery.Parse("Sonoran 52");

        query.Should().Satisfy<SearchQuery>(q =>
        {
            q.Hint.Should().BeNull();
            q.Terms.Should().Equal("SONORAN", "52");
        });
    }

    [Theory]
    [InlineData("52", "-00052")]
    [InlineData("00052", "-00052")]
    [InlineData("12345", "-12345")]
    [InlineData("123456", null)]
    public void NumberSuffix_PadsToTheSequenceWidth(string raw, string? suffix)
    {
        SearchQuery.Parse(raw).NumberSuffix.Should().Be(suffix);
    }

    [Theory]
    [InlineData("2x3", 2, 3)]
    [InlineData("1.5 x 1", 1.5, 1)]
    [InlineData("2 × 3", 2, 3)]
    [InlineData("4\" x 6\"", 4, 6)]
    [InlineData(".75 by 2", 0.75, 2)]
    public void Parse_LabelSize_ReadsAcrossAndAround(string raw, decimal across, decimal around)
    {
        SearchQuery.Parse(raw).Size.Should().Be((across, around));
    }

    [Fact]
    public void Parse_LongInput_IsCappedAndKeepsAtMostSixTerms()
    {
        var query = SearchQuery.Parse(string.Join(' ', Enumerable.Range(1, 40).Select(i => $"word{i}")));

        query.Should().Satisfy<SearchQuery>(q =>
        {
            q.Text.Length.Should().BeLessThanOrEqualTo(SearchQuery.MaxLength);
            q.Terms.Should().HaveCount(SearchQuery.MaxTerms);
        });
    }
}
