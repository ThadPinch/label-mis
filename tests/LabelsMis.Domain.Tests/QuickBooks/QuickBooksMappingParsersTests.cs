using System.Runtime.CompilerServices;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Invoicing;
using LabelsMis.Domain.QuickBooks;

namespace LabelsMis.Domain.Tests.QuickBooks;

/// <summary>Real answers from the Q8 sandbox run (2026-11-11, "Sandbox Company US ef89"), kept
/// beside the tests in <c>Fixtures/</c>. The project smoke folder's wrapped copies were unwrapped
/// to the body QuickBooks sent. Read from the source tree so the test project needs no copy step.</summary>
internal static class Q8
{
    public static string Read(string name, [CallerFilePath] string here = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(here)!, "Fixtures", name));
}

public class QuickBooksCustomFieldParserTests
{
    [Fact]
    public void FromInvoices_RecentInvoicesReadWithEnhancedFields_FindsThePoNumberField()
    {
        var (fields, invoiceCount) = QuickBooksCustomFieldParser.FromInvoices(Q8.Read("pd76-po-query-recent.json"));

        invoiceCount.Should().Be(40);
        fields.Should().ContainSingle().Which.Should().Be(
            new QuickBooksCustomFieldDefinition("1000000001", "P.O. Number", 3, Legacy: false));
    }

    [Fact]
    public void FromInvoices_SingleEnhancedRead_FindsTheField()
    {
        var (fields, invoiceCount) = QuickBooksCustomFieldParser.FromInvoices(Q8.Read("po-156-get-enhanced.json"));

        invoiceCount.Should().Be(1);
        fields.Should().ContainSingle().Which.DefinitionId.Should().Be("1000000001");
    }

    [Fact]
    public void FromInvoices_PlainRead_FindsNothing()
    {
        var (fields, invoiceCount) = QuickBooksCustomFieldParser.FromInvoices(Q8.Read("po-156-get-plain.json"));

        invoiceCount.Should().Be(1);
        fields.Should().BeEmpty();
    }

    [Fact]
    public void FromInvoices_LegacyIdWrittenWithoutName_IsLeftOut()
    {
        // c5: "PO-7129" written to DefinitionId "1" reads back with no name and never prints.
        var (fields, _) = QuickBooksCustomFieldParser.FromInvoices(Q8.Read("c5-readback.json"));

        fields.Should().BeEmpty();
    }

    [Fact]
    public void FromInvoices_EmptyQueryAnswer_FindsNothing()
    {
        var (fields, invoiceCount) = QuickBooksCustomFieldParser.FromInvoices("""{"QueryResponse":{},"time":"x"}""");

        invoiceCount.Should().Be(0);
        fields.Should().BeEmpty();
    }

    [Fact]
    public void FromInvoices_SeveralFields_PutsThePoLikeOneFirstThenTheMostUsed()
    {
        const string json = """
            {"QueryResponse":{"Invoice":[
              {"Id":"1","CustomField":[{"DefinitionId":"1000000002","Name":"Rep","Type":"StringType","StringValue":"AB"},
                                       {"DefinitionId":"1000000003","Name":"Job site","Type":"StringType","StringValue":"North"}]},
              {"Id":"2","CustomField":[{"DefinitionId":"1000000002","Name":"Rep","Type":"StringType","StringValue":"CD"},
                                       {"DefinitionId":"1000000001","Name":"PO #","Type":"StringType","StringValue":"7"}]}]}}
            """;

        var (fields, _) = QuickBooksCustomFieldParser.FromInvoices(json);

        fields.Select(f => (f.DefinitionId, f.InvoiceCount)).Should().Equal(
            ("1000000001", 1), ("1000000002", 2), ("1000000003", 1));
        QuickBooksCustomFieldParser.Suggest(fields)!.Name.Should().Be("PO #");
    }

    [Fact]
    public void FromPreferences_SandboxCompany_HasNoLegacyFieldSwitchedOn()
    {
        QuickBooksCustomFieldParser.FromPreferences(Q8.Read("discover-preferences.json")).Should().BeEmpty();
    }

    [Fact]
    public void FromPreferences_NamedLegacyFieldSwitchedOn_IsListedAsLegacy()
    {
        const string json = """
            {"Preferences":{"SalesFormsPrefs":{"CustomField":[
              {"CustomField":[{"Name":"SalesFormsPrefs.UseSalesCustom1","Type":"BooleanType","BooleanValue":true},
                              {"Name":"SalesFormsPrefs.UseSalesCustom2","Type":"BooleanType","BooleanValue":false}]},
              {"CustomField":[{"Name":"SalesFormsPrefs.SalesCustomName1","Type":"StringType","StringValue":"P.O. Number"},
                              {"Name":"SalesFormsPrefs.SalesCustomName2","Type":"StringType","StringValue":"Rep"}]}]}}}
            """;

        QuickBooksCustomFieldParser.FromPreferences(json).Should().Equal(
            new QuickBooksCustomFieldDefinition("1", "P.O. Number", 0, Legacy: true));
    }

    [Fact]
    public void Suggest_TwoPoLikeFields_SuggestsNeither()
    {
        var fields = new[]
        {
            new QuickBooksCustomFieldDefinition("1", "PO", 0, true),
            new QuickBooksCustomFieldDefinition("1000000001", "P.O. Number", 4, false)
        };

        QuickBooksCustomFieldParser.Suggest(fields).Should().BeNull();
    }
}

public class QuickBooksCatalogParserTests
{
    [Fact]
    public void ParsePage_SandboxItems_ReadsEveryItemWithItsTaxableFlag()
    {
        var (entries, rowCount) = QuickBooksCatalogParser.ParsePage(QuickBooksCatalogKind.Item, Q8.Read("discover-items.json"));

        rowCount.Should().Be(18);
        entries.Should().HaveCount(18);
        entries.Should().ContainEquivalentOf(new QuickBooksListEntry(QuickBooksCatalogKind.Item, "11", "Pump", "Pump", true));
        entries.Should().ContainEquivalentOf(new QuickBooksListEntry(QuickBooksCatalogKind.Item, "4", "Design", "Design", false));
    }

    [Fact]
    public void ParsePage_SandboxTerms_ReadsNamesWithoutTaxable()
    {
        var (entries, rowCount) = QuickBooksCatalogParser.ParsePage(QuickBooksCatalogKind.Term, Q8.Read("discover-terms.json"));

        rowCount.Should().Be(5);
        entries.Select(e => e.Name).Should().Equal("Due on receipt", "Net 10", "Net 15", "Net 30", "Net 60");
        entries.Should().OnlyContain(e => e.Taxable == null && e.FullName == e.Name);
    }

    [Fact]
    public void ParsePage_CompanyWithNoClasses_IsEmpty()
    {
        var (entries, rowCount) = QuickBooksCatalogParser.ParsePage(QuickBooksCatalogKind.Class, Q8.Read("discover-classes.json"));

        rowCount.Should().Be(0);
        entries.Should().BeEmpty();
    }

    [Fact]
    public void ParsePage_SubClassAndInactiveClass_KeepsTheFullNameAndDropsTheInactive()
    {
        const string json = """
            {"QueryResponse":{"Class":[
              {"Name":"LSQ8 Roll Label","SubClass":false,"FullyQualifiedName":"LSQ8 Roll Label","Active":true,"Id":"756489"},
              {"Name":"Sleeves","SubClass":true,"ParentRef":{"value":"756489"},"FullyQualifiedName":"LSQ8 Roll Label:Sleeves","Active":true,"Id":"756491"},
              {"Name":"Old","FullyQualifiedName":"Old","Active":false,"Id":"756492"}],"startPosition":1,"maxResults":3}}
            """;

        var (entries, rowCount) = QuickBooksCatalogParser.ParsePage(QuickBooksCatalogKind.Class, json);

        rowCount.Should().Be(3);
        entries.Select(e => e.FullName).Should().Equal("LSQ8 Roll Label", "LSQ8 Roll Label:Sleeves");
    }
}

public class QuickBooksTermsMatcherTests
{
    private static IReadOnlyList<string> SandboxTerms() =>
        QuickBooksCatalogParser.ParsePage(QuickBooksCatalogKind.Term, Q8.Read("discover-terms.json")).Entries
            .Select(e => e.Name).ToList();

    [Theory]
    [InlineData(PaymentTerms.DueOnReceipt, "Due on receipt")]
    [InlineData(PaymentTerms.Net15, "Net 15")]
    [InlineData(PaymentTerms.Net30, "Net 30")]
    [InlineData(PaymentTerms.Net60, "Net 60")]
    [InlineData(PaymentTerms.Net90, null)]
    [InlineData(PaymentTerms.Cod, null)]
    [InlineData(PaymentTerms.Prepay, null)]
    public void Match_SandboxTerms_PairsByNameIgnoringCase(PaymentTerms terms, string? expected)
    {
        QuickBooksTermsMatcher.Match(terms, SandboxTerms()).Should().Be(expected);
    }

    [Theory]
    [InlineData(PaymentTerms.Cod, "C.O.D.")]
    [InlineData(PaymentTerms.Prepay, "Prepaid")]
    [InlineData(PaymentTerms.DueOnReceipt, "Due upon receipt")]
    [InlineData(PaymentTerms.Net90, "Net 90 days")]
    public void Match_CommonSpelling_Pairs(PaymentTerms terms, string quickBooksName)
    {
        QuickBooksTermsMatcher.Match(terms, ["Net 10", quickBooksName]).Should().Be(quickBooksName);
    }

    [Fact]
    public void Match_TwoTermsWithTheSameName_PairsNeither()
    {
        QuickBooksTermsMatcher.Match(PaymentTerms.Net30, ["Net 30", "Net30"]).Should().BeNull();
    }

    [Fact]
    public void FillBlanks_KeepsChosenTermsAndFillsTheRestByName()
    {
        var saved = new Dictionary<PaymentTerms, string> { [PaymentTerms.Net30] = "Net 10", [PaymentTerms.Net15] = " " };

        var filled = QuickBooksTermsMatcher.FillBlanks(saved, SandboxTerms());

        filled.Should().BeEquivalentTo(new Dictionary<PaymentTerms, string>
        {
            [PaymentTerms.DueOnReceipt] = "Due on receipt",
            [PaymentTerms.Net15] = "Net 15",
            [PaymentTerms.Net30] = "Net 10",
            [PaymentTerms.Net60] = "Net 60"
        });
    }
}

public class QuickBooksMappingCheckTests
{
    private static readonly QuickBooksCatalogLookup Lookup = new(
    [
        new(QuickBooksCatalogKind.Item, "1", "Sales", "Sales", true),
        new(QuickBooksCatalogKind.Item, "11", "Pump", "Design:Fountains:Pump", true),
        new(QuickBooksCatalogKind.Class, "756489", "Roll Label", "Roll Label")
    ]);

    [Theory]
    [InlineData("Sales", "Sales", QuickBooksNameStatus.Found)]
    [InlineData(" sales ", "Sales", QuickBooksNameStatus.Matched)]
    [InlineData("pump", "Design:Fountains:Pump", QuickBooksNameStatus.Matched)]
    [InlineData("DESIGN:fountains:pump", "Design:Fountains:Pump", QuickBooksNameStatus.Matched)]
    [InlineData("Shipping", "Shipping", QuickBooksNameStatus.Missing)]
    [InlineData("", "", QuickBooksNameStatus.Missing)]
    public void Check_AgainstTheItemList(string saved, string selected, QuickBooksNameStatus status)
    {
        QuickBooksNameCheck.Check(Lookup, QuickBooksCatalogKind.Item, saved)
            .Should().Be(new QuickBooksNameCheck(saved.Trim(), selected, status));
    }

    [Fact]
    public void Check_ListNeverRead_IsNotChecked()
    {
        QuickBooksNameCheck.Check(Lookup, QuickBooksCatalogKind.Term, "Net 30").Status
            .Should().Be(QuickBooksNameStatus.NotChecked);
    }

    [Fact]
    public void Check_DefaultMapping_FlagsEveryNameTheCompanyLacks()
    {
        var checks = QuickBooksMappingSlots.Check(QuickBooksMapping.Default, Lookup);

        checks.Should().HaveCount(8);
        checks.Where(c => c.Value.IsMissing).Select(c => c.Key).Should().BeEquivalentTo(
            "ShippingItem", "DesignItem", "RushItem", "DieCreationItem", "ShrinkSleevesClass", "BuyoutClass");
    }

    [Fact]
    public void Normalise_TakesQuickBooksSpellingAndKeepsUnknownNames()
    {
        var mapping = QuickBooksMapping.Default with { SalesItem = "sales", DesignItem = "pump", RollLabelClass = "roll label" };

        var normalised = QuickBooksMappingSlots.Normalise(mapping, Lookup);

        normalised.Should().Be(mapping with
        {
            SalesItem = "Sales",
            DesignItem = "Design:Fountains:Pump",
            RollLabelClass = "Roll Label"
        });
    }
}
