using LabelsMis.Domain.QuickBooks;

namespace LabelsMis.Domain.Tests.QuickBooks;

public class QuickBooksCustomerMatchingTests
{
    [Theory]
    [InlineData("Acme, Inc.", "ACME INC")]
    [InlineData("Smith & Sons", "Smith and Sons")]
    [InlineData("  Hey   Lucky  Label ", "hey lucky label")]
    [InlineData("Hey-Lucky", "Hey Lucky")]
    [InlineData("O'Brien Farms", "OBrien Farms")]
    [InlineData("Café Rouge", "Cafe Rouge")]
    [InlineData("Amy's Bird Sanctuary", "amys bird sanctuary")]
    public void Normalize_IgnoresCaseSpacesPunctuationAndAccents(string a, string b)
    {
        QuickBooksCustomerNames.Normalize(a).Should().Be(QuickBooksCustomerNames.Normalize(b));
        QuickBooksCustomerNames.Same(a, b).Should().BeTrue();
    }

    [Theory]
    [InlineData("Acme Inc", "Acme LLC")]
    [InlineData("Acme 1", "Acme 2")]
    [InlineData("Red Rock Diner", "Red Rock Diner East")]
    public void Normalize_KeepsLettersAndDigitsApart(string a, string b)
    {
        QuickBooksCustomerNames.Same(a, b).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" -- ")]
    public void Same_TwoNamesWithNothingInThem_AreNotTheSame(string? name)
    {
        QuickBooksCustomerNames.Normalize(name).Should().BeEmpty();
        QuickBooksCustomerNames.Same(name, name).Should().BeFalse();
    }

    private static readonly IReadOnlyList<QuickBooksCustomerSummary> Sandbox =
        QuickBooksCustomerParser.ParsePage(QuickBooksCustomerParserTests.SandboxPage).Customers;

    [Fact]
    public void Match_ProposesTheOneQuickBooksCustomerWithTheSameName()
    {
        var id = Guid.NewGuid();

        var match = QuickBooksCustomerMatcher.Match([new UnlinkedCustomer(id, "AMY'S BIRD SANCTUARY")], Sandbox, []).Single();

        match.Should().BeEquivalentTo(new QuickBooksCustomerMatch(
            id, "AMY'S BIRD SANCTUARY", QuickBooksMatchKind.Exact, Sandbox[1], [Sandbox[1]], QuickBooksMatchField.DisplayName));
    }

    [Fact]
    public void Match_PrefersTheDisplayNameOverSubCustomersSharingTheCompanyName()
    {
        // Freeman Sporting Goods (7) and its two jobs all carry CompanyName "Freeman Sporting Goods".
        var match = QuickBooksCustomerMatcher.Match([new UnlinkedCustomer(Guid.NewGuid(), "Freeman Sporting Goods")], Sandbox, []).Single();

        match.Kind.Should().Be(QuickBooksMatchKind.Exact);
        match.Proposed!.Id.Should().Be("7");
    }

    [Fact]
    public void Match_FallsBackToTheCompanyNameOfATopLevelCustomer()
    {
        var quickBooks = new[] { new QuickBooksCustomerSummary("40", "Jane Doe", CompanyName: "Doe Labels LLC") };

        var match = QuickBooksCustomerMatcher.Match([new UnlinkedCustomer(Guid.NewGuid(), "Doe Labels, LLC")], quickBooks, []).Single();

        match.Kind.Should().Be(QuickBooksMatchKind.Exact);
        match.Proposed!.Id.Should().Be("40");
        match.MatchedOn.Should().Be(QuickBooksMatchField.CompanyName);
    }

    [Fact]
    public void Match_NeverMatchesTheCompanyNameOfASubCustomer()
    {
        var quickBooks = new[] { new QuickBooksCustomerSummary("41", "Warehouse job", CompanyName: "Doe Labels", IsSubCustomer: true) };

        var match = QuickBooksCustomerMatcher.Match([new UnlinkedCustomer(Guid.NewGuid(), "Doe Labels")], quickBooks, []).Single();

        match.Kind.Should().Be(QuickBooksMatchKind.None);
        match.Candidates.Should().BeEmpty();
    }

    [Fact]
    public void Match_DoesNotProposeACustomerAlreadyLinkedElsewhere()
    {
        var match = QuickBooksCustomerMatcher.Match([new UnlinkedCustomer(Guid.NewGuid(), "Amy's Bird Sanctuary")], Sandbox, ["1"]).Single();

        match.Kind.Should().Be(QuickBooksMatchKind.None);
        match.Proposed.Should().BeNull();
    }

    [Fact]
    public void Match_DoesNotProposeAnInactiveCustomer()
    {
        var quickBooks = new[] { new QuickBooksCustomerSummary("50", "Old Co", Active: false) };

        QuickBooksCustomerMatcher.Match([new UnlinkedCustomer(Guid.NewGuid(), "Old Co")], quickBooks, []).Single()
            .Kind.Should().Be(QuickBooksMatchKind.None);
    }

    [Fact]
    public void Match_TwoQuickBooksCustomersWithTheSameNormalisedName_AreAPersonsChoice()
    {
        var quickBooks = new[]
        {
            new QuickBooksCustomerSummary("60", "Acme, Inc."),
            new QuickBooksCustomerSummary("61", "ACME INC")
        };

        var match = QuickBooksCustomerMatcher.Match([new UnlinkedCustomer(Guid.NewGuid(), "Acme Inc")], quickBooks, []).Single();

        match.Kind.Should().Be(QuickBooksMatchKind.Ambiguous);
        match.Proposed.Should().BeNull();
        match.Candidates.Select(c => c.Id).Should().BeEquivalentTo(["60", "61"]);
    }

    [Fact]
    public void Match_OneQuickBooksCustomerFittingTwoLabelShopCustomers_IsProposedToNeither()
    {
        var a = new UnlinkedCustomer(Guid.NewGuid(), "Cool Cars");
        var b = new UnlinkedCustomer(Guid.NewGuid(), "Cool Cars, Inc");
        var c = new UnlinkedCustomer(Guid.NewGuid(), "COOL CARS");
        var quickBooks = new[] { new QuickBooksCustomerSummary("3", "Cool Cars") };

        var matches = QuickBooksCustomerMatcher.Match([a, b, c], quickBooks, []);

        matches.Single(m => m.CustomerId == a.Id).Kind.Should().Be(QuickBooksMatchKind.Ambiguous);
        matches.Single(m => m.CustomerId == c.Id).Kind.Should().Be(QuickBooksMatchKind.Ambiguous);
        matches.Single(m => m.CustomerId == a.Id).Candidates.Should().ContainSingle().Which.Id.Should().Be("3");
        matches.Single(m => m.CustomerId == b.Id).Kind.Should().Be(QuickBooksMatchKind.None);
    }

    [Fact]
    public void Match_KeepsTheOrderOfTheLabelShopCustomers()
    {
        var names = new[] { "Zed", "Amy's Bird Sanctuary", "Dylan Sollfrank" };

        var matches = QuickBooksCustomerMatcher.Match(names.Select(n => new UnlinkedCustomer(Guid.NewGuid(), n)), Sandbox, []);

        matches.Select(m => (m.Name, m.Kind)).Should().Equal(
            ("Zed", QuickBooksMatchKind.None),
            ("Amy's Bird Sanctuary", QuickBooksMatchKind.Exact),
            ("Dylan Sollfrank", QuickBooksMatchKind.Exact));
    }
}
