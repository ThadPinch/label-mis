using LabelsMis.Domain.QuickBooks;

namespace LabelsMis.Domain.Tests.QuickBooks;

public class QuickBooksCustomerParserTests
{
    /// <summary>Four rows of the sandbox company's <c>select * from Customer</c> answer
    /// (q8/responses/discover-customers.json), trimmed to the fields the pages read.</summary>
    internal const string SandboxPage = """
        {"QueryResponse":{"Customer":[
          {"Id":"8","DisplayName":"0969 Ocean View Road","CompanyName":"Freeman Sporting Goods",
           "FullyQualifiedName":"Freeman Sporting Goods:0969 Ocean View Road","Job":true,"ParentRef":{"value":"7"},"Active":true,
           "PrimaryEmailAddr":{"Address":"Sporting_goods@intuit.com"},"PrimaryPhone":{"FreeFormNumber":"(415) 555-9933"},
           "BillAddr":{"Line1":"370 Easy St.","City":"Middlefield","CountrySubDivisionCode":"CA","PostalCode":"94482"}},
          {"Id":"1","DisplayName":"Amy's Bird Sanctuary","CompanyName":"Amy's Bird Sanctuary",
           "FullyQualifiedName":"Amy's Bird Sanctuary","Job":false,"Active":true,
           "PrimaryEmailAddr":{"Address":"Birds@Intuit.com"},"PrimaryPhone":{"FreeFormNumber":"(650) 555-3311"},
           "BillAddr":{"Line1":"4581 Finch St.","City":"Bayshore","CountrySubDivisionCode":"CA","PostalCode":"94326"}},
          {"Id":"6","DisplayName":"Dylan Sollfrank","FullyQualifiedName":"Dylan Sollfrank","Job":false,"Active":true},
          {"Id":"7","DisplayName":"Freeman Sporting Goods","CompanyName":"Freeman Sporting Goods",
           "FullyQualifiedName":"Freeman Sporting Goods","Job":false,"Active":true,
           "PrimaryEmailAddr":{"Address":"Sporting_goods@intuit.com"},"PrimaryPhone":{"FreeFormNumber":"(650) 555-0987"},
           "BillAddr":{"Line1":"370 Easy St.","City":"Middlefield","CountrySubDivisionCode":"CA","PostalCode":"94482"}}],
         "startPosition":1,"maxResults":4},"time":"2026-11-10T21:58:03.214-08:00"}
        """;

    [Fact]
    public void ParsePage_ReadsEveryCustomerOfTheSandboxAnswer()
    {
        var (customers, rows) = QuickBooksCustomerParser.ParsePage(SandboxPage);

        rows.Should().Be(4);
        customers.Select(c => c.Id).Should().Equal("8", "1", "6", "7");
        customers[1].Should().Be(new QuickBooksCustomerSummary(
            "1", "Amy's Bird Sanctuary", "Amy's Bird Sanctuary", "Amy's Bird Sanctuary",
            IsSubCustomer: false, Active: true, Email: "Birds@Intuit.com", Phone: "(650) 555-3311", Place: "Bayshore, CA"));
    }

    [Fact]
    public void ParsePage_MarksASubCustomerAndKeepsItsParentInTheFullName()
    {
        var job = QuickBooksCustomerParser.ParsePage(SandboxPage).Customers[0];

        job.IsSubCustomer.Should().BeTrue();
        job.FullyQualifiedName.Should().Be("Freeman Sporting Goods:0969 Ocean View Road");
    }

    [Fact]
    public void ParsePage_ACustomerWithNoCompanyEmailOrAddress_HasThemNull()
    {
        var dylan = QuickBooksCustomerParser.ParsePage(SandboxPage).Customers[2];

        dylan.Should().Be(new QuickBooksCustomerSummary("6", "Dylan Sollfrank", null, "Dylan Sollfrank"));
    }

    [Theory]
    [InlineData("""{"QueryResponse":{}}""")]
    [InlineData("""{"QueryResponse":{"Customer":"x"}}""")]
    [InlineData("""{}""")]
    [InlineData("""[]""")]
    public void ParsePage_WithNoCustomers_IsEmpty(string json)
    {
        var (customers, rows) = QuickBooksCustomerParser.ParsePage(json);

        customers.Should().BeEmpty();
        rows.Should().Be(0);
    }

    [Fact]
    public void ParsePage_CountsARowItCannotUse_SoPagingStillSeesAFullPage()
    {
        var (customers, rows) = QuickBooksCustomerParser.ParsePage(
            """{"QueryResponse":{"Customer":[{"Id":"1","DisplayName":"A"},{"Id":"2"},{"Id":"3","DisplayName":"C","Active":false}]}}""");

        rows.Should().Be(3);
        customers.Select(c => (c.Id, c.Active)).Should().Equal(("1", true), ("3", false));
    }

    [Fact]
    public void ParseOne_ReadsTheSandboxCreateAnswer()
    {
        // q8/responses/fixture-customer-lsq8-terms-customer.json, trimmed.
        const string json = """
            {"Customer":{"Taxable":true,"Job":false,"BillWithParent":false,"SalesTermRef":{"value":"2","name":"Net 15"},
             "Balance":0,"domain":"QBO","sparse":false,"Id":"58","SyncToken":"0",
             "FullyQualifiedName":"LSQ8 Terms Customer","DisplayName":"LSQ8 Terms Customer","PrintOnCheckName":"LSQ8 Terms Customer",
             "Active":true,"PrimaryEmailAddr":{"Address":"lsq8-terms@example.com"},"DefaultTaxCodeRef":{"value":"2"}},
             "time":"2026-11-10T22:08:28.454-08:00"}
            """;

        QuickBooksCustomerParser.ParseOne(json).Should().Be(new QuickBooksCustomerSummary(
            "58", "LSQ8 Terms Customer", null, "LSQ8 Terms Customer", Email: "lsq8-terms@example.com"));
    }

    [Fact]
    public void ParseOne_WithoutACustomer_IsNull()
    {
        QuickBooksCustomerParser.ParseOne("""{"time":"x"}""").Should().BeNull();
    }
}

public class QuickBooksCustomerQueriesTests
{
    [Fact]
    public void Search_LooksForActiveCustomersWhoseDisplayNameContainsTheText()
    {
        QuickBooksCustomerQueries.Search("  acme ").Should().Be(
            "select * from Customer where Active = true and DisplayName like '%acme%' startposition 1 maxresults 20");
    }

    [Fact]
    public void Search_EscapesQuotesAndDropsWildcards()
    {
        QuickBooksCustomerQueries.Search("Amy's 100%").Should().Contain("like '%Amy\\'s 100%'");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("%%")]
    public void Search_WithNothingToSearchFor_IsNull(string? text)
    {
        QuickBooksCustomerQueries.Search(text).Should().BeNull();
    }

    [Fact]
    public void Search_CutsLongTextTo100Characters()
    {
        QuickBooksCustomerQueries.CleanSearchText(new string('a', 150)).Should().HaveLength(100);
    }

    [Fact]
    public void ActivePage_AsksForAThousandFromThePosition()
    {
        QuickBooksCustomerQueries.ActivePage(1001).Should().Be(
            "select * from Customer where Active = true startposition 1001 maxresults 1000");
    }

    [Fact]
    public void ByDisplayName_EscapesQuotes()
    {
        QuickBooksCustomerQueries.ByDisplayName(" Amy's Bird Sanctuary ").Should().Be(
            "select * from Customer where DisplayName = 'Amy\\'s Bird Sanctuary'");
    }

    [Fact]
    public void Operations_DescribeEachCustomerCall()
    {
        new[] { QuickBooksCustomerOperations.Search, QuickBooksCustomerOperations.Read,
                QuickBooksCustomerOperations.Create, QuickBooksCustomerOperations.List }
            .Select(QuickBooksCustomerOperations.Describe)
            .Should().OnlyContain(d => !string.IsNullOrWhiteSpace(d));
        QuickBooksCustomerOperations.Describe(QuickBooksOperations.Connect).Should().BeNull();
    }
}
