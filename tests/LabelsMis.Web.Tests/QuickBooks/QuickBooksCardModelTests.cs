using LabelsMis.Domain.Enums;
using LabelsMis.Web.Pages.Invoices;
using LabelsMis.Web.Services.QuickBooks;

namespace LabelsMis.Web.Tests.QuickBooks;

public class QuickBooksCardModelTests
{
    private static QuickBooksSendState State(
        QuickBooksExportStatus status = QuickBooksExportStatus.None,
        string? quickBooksId = null,
        bool voided = false,
        decimal? total = null,
        decimal? tax = null) =>
        new(Guid.NewGuid(), status, quickBooksId is not null, voided, quickBooksId, null, 0, null, null,
            null, null, total, tax, false, null);

    private static QuickBooksCardModel Card(
        QuickBooksSendState? state = null,
        QuickBooksConnectionState connection = QuickBooksConnectionState.Connected,
        bool canEdit = true,
        bool isVoid = false,
        bool nonBillable = false,
        bool exportedByHand = false,
        IReadOnlyList<string>? waiting = null,
        bool compact = false) =>
        new(Guid.NewGuid(), "INV-1", state, connection, canEdit, isVoid, nonBillable, exportedByHand,
            waiting ?? [], 100m, 8m, utc => utc.ToString("O"), compact);

    [Fact]
    public void ReadyInvoice_CanBeSentLinkedAndGivenEmailOptions()
    {
        var card = Card(State());

        card.Should().Match<QuickBooksCardModel>(c =>
            c.CanSend && !c.SendBlocked && c.CanLinkExisting && c.CanSetSendOptions
            && !c.CanRetry && !c.CanCancel && !c.CanVoidInQuickBooks);
    }

    [Fact]
    public void WaitingInvoice_ShowsSendDisabled()
    {
        var card = Card(State(), waiting: ["Waiting for shipping."]);

        card.Should().Match<QuickBooksCardModel>(c => c.CanSend && c.SendBlocked);
    }

    [Theory]
    [InlineData(QuickBooksConnectionState.NotConnected)]
    [InlineData(QuickBooksConnectionState.NeedsReconnect)]
    [InlineData(QuickBooksConnectionState.NoCredentials)]
    public void NotConnected_OffersNothingThatTalksToQuickBooks(QuickBooksConnectionState connection)
    {
        var card = Card(State(QuickBooksExportStatus.NeedsAttention), connection);

        card.Should().Match<QuickBooksCardModel>(c =>
            !c.CanSend && !c.CanRetry && !c.CanLinkExisting && c.CanCancel && c.CanSetSendOptions);
    }

    [Fact]
    public void ReadOnlyUser_GetsNoButtons()
    {
        var card = Card(State(QuickBooksExportStatus.NeedsAttention), canEdit: false);

        card.Should().Match<QuickBooksCardModel>(c =>
            !c.CanSend && !c.CanRetry && !c.CanCancel && !c.CanLinkExisting && !c.CanSetSendOptions);
    }

    [Fact]
    public void Queued_CanBeTakenOutOfLineOnly()
    {
        var card = Card(State(QuickBooksExportStatus.Queued));

        card.Should().Match<QuickBooksCardModel>(c =>
            c.CanCancel && !c.CanSend && !c.CanRetry && !c.CanLinkExisting && c.CanSetSendOptions);
    }

    [Fact]
    public void Sending_OffersNothing()
    {
        var card = Card(State(QuickBooksExportStatus.Sending));

        card.Should().Match<QuickBooksCardModel>(c =>
            !c.CanCancel && !c.CanSend && !c.CanRetry && !c.CanLinkExisting && !c.CanSetSendOptions);
    }

    [Fact]
    public void InQuickBooks_ShowsTheDifferenceAndNoSendOrOptions()
    {
        var card = Card(State(quickBooksId: "130", total: 108.25m, tax: 8m));

        card.Should().Match<QuickBooksCardModel>(c =>
            !c.CanSend && !c.CanLinkExisting && !c.CanSetSendOptions
            && c.TotalDifference == 8.25m && c.TaxDifference == 0m);
    }

    [Fact]
    public void ExportedByHand_CanBeLinkedButNotSent()
    {
        var card = Card(State(), exportedByHand: true);

        card.Should().Match<QuickBooksCardModel>(c => !c.CanSend && c.CanLinkExisting);
    }

    [Fact]
    public void VoidInvoiceStillLiveInQuickBooks_CanBeVoidedThere()
    {
        Card(State(quickBooksId: "130"), isVoid: true).CanVoidInQuickBooks.Should().BeTrue();
        Card(State(quickBooksId: "130", voided: true), isVoid: true).CanVoidInQuickBooks.Should().BeFalse();
        Card(State(), isVoid: true).CanVoidInQuickBooks.Should().BeFalse();
    }

    [Fact]
    public void NonBillable_OffersNothing()
    {
        var card = Card(State(), nonBillable: true);

        card.Should().Match<QuickBooksCardModel>(c => !c.CanSend && !c.CanLinkExisting && !c.CanSetSendOptions);
    }

    [Fact]
    public void Compact_LeavesLinkAndOptionsToTheInvoicePage()
    {
        var card = Card(State(), compact: true);

        card.Should().Match<QuickBooksCardModel>(c => c.CanSend && !c.CanLinkExisting && !c.CanSetSendOptions);
    }
}
