using System.Text.Json;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Invoicing;
using LabelsMis.Domain.QuickBooks;
using LabelsMis.Web.Pages.Settings;
using LabelsMis.Web.Services.QuickBooks;

namespace LabelsMis.Web.Tests.QuickBooks;

/// <summary>The parts of Setup → QuickBooks that decide what the boxes show; no database.</summary>
public class QuickBooksSettingsPageTests
{
    private static readonly QuickBooksCatalogLookup SandboxLists = new(
    [
        new(QuickBooksCatalogKind.Term, "1", "Due on receipt", "Due on receipt"),
        new(QuickBooksCatalogKind.Term, "5", "Net 10", "Net 10"),
        new(QuickBooksCatalogKind.Term, "3", "Net 30", "Net 30"),
        new(QuickBooksCatalogKind.Item, "1", "Services", "Services", false)
    ]);

    private static QuickBooksTermRow Row(IReadOnlyList<QuickBooksTermRow> rows, PaymentTerms terms) =>
        rows.Single(r => r.Terms == terms);

    [Fact]
    public void TermRows_NothingSaved_PreselectTheSameNamedQuickBooksTerm()
    {
        var rows = QuickBooksTermRow.Build(QuickBooksPushSettings.Default, SandboxLists);

        rows.Select(r => r.Terms).Should().Equal(Enum.GetValues<PaymentTerms>());
        var dueOnReceipt = Row(rows, PaymentTerms.DueOnReceipt).Picker;
        dueOnReceipt.Name.Should().Be("Sending.Terms[DueOnReceipt]");
        dueOnReceipt.Check.Selected.Should().Be("Due on receipt");
        dueOnReceipt.Note.Should().StartWith("Matched by name");
        dueOnReceipt.Options.Should().Equal("Due on receipt", "Net 10", "Net 30");
        var net60 = Row(rows, PaymentTerms.Net60).Picker;
        net60.Check.Selected.Should().BeEmpty("QuickBooks has no Net 60 term, so no terms is pre-selected");
        net60.NoneText.Should().Be(QuickBooksTermRow.NoneText);
    }

    [Fact]
    public void TermRows_SavedChoices_AreKeptAndANameQuickBooksLacksIsFlagged()
    {
        var push = QuickBooksPushSettings.Default with
        {
            Terms = new Dictionary<PaymentTerms, string> { [PaymentTerms.Net30] = "Net 10", [PaymentTerms.Net60] = "Net 60" }
        };

        var rows = QuickBooksTermRow.Build(push, SandboxLists);

        Row(rows, PaymentTerms.Net30).Picker.Check.Should().Be(new QuickBooksNameCheck("Net 10", "Net 10", QuickBooksNameStatus.Found));
        Row(rows, PaymentTerms.Net30).Picker.Note.Should().BeNull();
        Row(rows, PaymentTerms.Net60).Picker.Check.IsMissing.Should().BeTrue();
    }

    [Fact]
    public void TermRows_ListsNeverRead_AreTextBoxesWithTheSavedName()
    {
        var push = QuickBooksPushSettings.Default with { Terms = new Dictionary<PaymentTerms, string> { [PaymentTerms.Net15] = "Net 15" } };

        var rows = QuickBooksTermRow.Build(push, QuickBooksCatalogLookup.Empty);

        rows.Should().OnlyContain(r => r.Picker.Options.Count == 0);
        Row(rows, PaymentTerms.Net15).Picker.Check.Should().Be(new QuickBooksNameCheck("Net 15", "Net 15", QuickBooksNameStatus.NotChecked));
        Row(rows, PaymentTerms.Net30).Picker.Check.Selected.Should().BeEmpty();
    }

    [Fact]
    public void PoFieldSearch_SurvivesTheTripThroughTempData()
    {
        var search = new QuickBooksCustomFieldSearch(
            [new QuickBooksCustomFieldDefinition("1000000001", "P.O. Number", 3, Legacy: false)], InvoicesRead: 40, FromPreferences: false);

        var back = JsonSerializer.Deserialize<QuickBooksCustomFieldSearch>(JsonSerializer.Serialize(search))!;

        back.Fields.Should().Equal(search.Fields);
        (back.InvoicesRead, back.FromPreferences).Should().Be((40, false));
        back.Suggested!.DefinitionId.Should().Be("1000000001");
    }
}
