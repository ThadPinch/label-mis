using LabelsMis.Domain.Enums;
using LabelsMis.Web.Pages.Invoices;
using LabelsMis.Web.Services.QuickBooks;

namespace LabelsMis.Web.Tests.QuickBooks;

public class QuickBooksUiTests
{
    private static QuickBooksSendState State(
        QuickBooksExportStatus status,
        bool inQuickBooks = false,
        bool voided = false,
        string? lastError = null,
        string? fixUrl = null) =>
        new(Guid.NewGuid(), status, inQuickBooks, voided, inQuickBooks ? "42" : null, null, 0, null, null,
            lastError, fixUrl, null, null, false, null);

    [Theory]
    [InlineData(QuickBooksExportStatus.Queued, false, false, "text-bg-info")]
    [InlineData(QuickBooksExportStatus.Sending, false, false, "text-bg-primary")]
    [InlineData(QuickBooksExportStatus.NeedsAttention, true, false, "text-bg-danger")]
    [InlineData(QuickBooksExportStatus.None, true, true, "text-bg-dark")]
    [InlineData(QuickBooksExportStatus.None, true, false, "text-bg-success")]
    [InlineData(QuickBooksExportStatus.None, false, false, "d-none")]
    public void BadgeClass_FollowsTheState(QuickBooksExportStatus status, bool inQuickBooks, bool voided, string expected)
    {
        QuickBooksUi.BadgeClass(State(status, inQuickBooks, voided)).Should().Be(expected);
    }

    [Fact]
    public void BadgeClass_WhenNoState_IsHidden()
    {
        QuickBooksUi.BadgeClass(null).Should().Be("d-none");
    }

    [Fact]
    public void Badge_CarriesWhatThePollNeeds()
    {
        var state = State(QuickBooksExportStatus.Queued);
        using var writer = new StringWriter();
        QuickBooksUi.Badge(state.InvoiceId, state).WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);

        writer.ToString().Should()
            .Contain($"data-invoice-id=\"{state.InvoiceId}\"").And
            .Contain("data-busy=\"true\"").And
            .Contain("data-qb-badge").And
            .Contain(">Queued<");
    }

    [Fact]
    public void Summarise_WhenOneQueued_NamesIt()
    {
        var (queued, problems) = QuickBooksUi.Summarise([new QuickBooksQueueOutcome(Guid.NewGuid(), "INV-1", true, null)]);

        queued.Should().Be("INV-1 is on its way to QuickBooks.");
        problems.Should().BeEmpty();
    }

    [Fact]
    public void Summarise_WhenSomeRefused_CountsTheRestAndListsEachReason()
    {
        var (queued, problems) = QuickBooksUi.Summarise(
        [
            new QuickBooksQueueOutcome(Guid.NewGuid(), "INV-1", true, null),
            new QuickBooksQueueOutcome(Guid.NewGuid(), "INV-2", true, null),
            new QuickBooksQueueOutcome(Guid.NewGuid(), "INV-3", false, "Waiting for shipping."),
            new QuickBooksQueueOutcome(Guid.NewGuid(), "INV-4", false, null)
        ]);

        queued.Should().Be("2 invoices are on their way to QuickBooks.");
        problems.Should().Equal("INV-3: Waiting for shipping.", "INV-4: not sent.");
    }

    [Fact]
    public void Summarise_WhenNoneQueued_HasNoSuccessLine()
    {
        var (queued, problems) = QuickBooksUi.Summarise(
            [new QuickBooksQueueOutcome(Guid.NewGuid(), "INV-3", false, "QuickBooks is not connected.")]);

        queued.Should().BeNull();
        problems.Should().ContainSingle();
    }

    [Fact]
    public void ToJson_CarriesTheProblemAndItsFix()
    {
        var state = State(QuickBooksExportStatus.NeedsAttention, lastError: "Pick a class.", fixUrl: "/settings/quickbooks");

        var json = QuickBooksUi.ToJson(state);

        json.Should().Be(new QuickBooksStateJson(
            state.InvoiceId, "Needs attention", "text-bg-danger", false, false, true, "Pick a class.", "/settings/quickbooks"));
    }

    [Theory]
    [InlineData(100.00, null, null)]
    [InlineData(100.00, 100.00, 0.00)]
    [InlineData(100.00, 108.25, 8.25)]
    public void Difference_IsQuickBooksMinusLabelShop(double labelShop, double? quickBooks, double? expected)
    {
        QuickBooksUi.Difference((decimal)labelShop, (decimal?)quickBooks)
            .Should().Be((decimal?)expected);
    }
}
