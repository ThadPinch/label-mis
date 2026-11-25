using FluentAssertions;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.QuickBooks;

namespace LabelsMis.Domain.Tests.Entities;

/// <summary>An invoice is dated the day it is sent to QuickBooks, not the day its order was entered.</summary>
public class InvoiceQuickBooksDateTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 25, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Entered = new(2026, 11, 3);
    private static readonly DateOnly Today = new(2026, 11, 25);

    private static Invoice Draft() =>
        Invoice.CreateDraft(Guid.NewGuid(), "INV-2026-00001", Guid.NewGuid(), Guid.NewGuid(), null,
            Entered, Entered.AddDays(30), 100m, 0m, 0m, null, UserId, Now);

    [Theory]
    [InlineData(PaymentTerms.DueOnReceipt, 0)]
    [InlineData(PaymentTerms.Prepay, 0)]
    [InlineData(PaymentTerms.Cod, 0)]
    [InlineData(PaymentTerms.Net15, 15)]
    [InlineData(PaymentTerms.Net30, 30)]
    public void DateForQuickBooks_NewToQuickBooks_TakesTodayAndCountsTermsFromIt(PaymentTerms terms, int days)
    {
        var invoice = Draft();

        invoice.IsDatedWhenSent.Should().BeTrue();
        invoice.DateForQuickBooks(Today, terms).Should().BeTrue();

        invoice.InvoiceDate.Should().Be(Today);
        invoice.DueDate.Should().Be(Today.AddDays(days));
    }

    [Fact]
    public void DateForQuickBooks_AlreadyInQuickBooks_KeepsTheDateTheCustomerHasSeen()
    {
        var invoice = Draft();
        invoice.DateForQuickBooks(Today, PaymentTerms.Net30);
        invoice.RecordInQuickBooks("200", "0", 100m, 0m, null, UserId, Now);
        invoice.UnmarkExported(UserId, Now);

        invoice.IsDatedWhenSent.Should().BeFalse();
        invoice.DateForQuickBooks(Today.AddDays(9), PaymentTerms.Net30).Should().BeFalse();

        invoice.InvoiceDate.Should().Be(Today);
        invoice.DueDate.Should().Be(Today.AddDays(30));
    }

    [Fact]
    public void DateForQuickBooks_MarkedExportedByHandThenUnmarked_IsDatedWhenItIsSent()
    {
        var invoice = Draft();
        invoice.MarkExported(Now, UserId, Now);
        invoice.IsDatedWhenSent.Should().BeFalse("it is not going to be sent");

        invoice.UnmarkExported(UserId, Now);

        invoice.DateForQuickBooks(Today, PaymentTerms.DueOnReceipt).Should().BeTrue();
        invoice.DueDate.Should().Be(Today);
    }

    [Fact]
    public void DateForQuickBooks_Void_ChangesNothing()
    {
        var invoice = Draft();
        invoice.Void("entered twice", UserId, Now);

        invoice.DateForQuickBooks(Today, PaymentTerms.Net30).Should().BeFalse();

        invoice.InvoiceDate.Should().Be(Entered);
    }

    [Fact]
    public void TakeQuickBooksDates_UsesWhatQuickBooksHas_AndKeepsWhatItDidNotSend()
    {
        var invoice = Draft();

        invoice.TakeQuickBooksDates(new DateOnly(2026, 11, 20), null);

        invoice.InvoiceDate.Should().Be(new DateOnly(2026, 11, 20));
        invoice.DueDate.Should().Be(Entered.AddDays(30));

        invoice.TakeQuickBooksDates(null, new DateOnly(2026, 12, 5));

        invoice.InvoiceDate.Should().Be(new DateOnly(2026, 11, 20));
        invoice.DueDate.Should().Be(new DateOnly(2026, 12, 5));
    }

    [Fact]
    public void Answer_ReadsQuickBooksDates_AndIgnoresOnesItCannotRead()
    {
        var answer = QuickBooksInvoiceAnswer.FromEntity(
            """{"Invoice":{"Id":"200","SyncToken":"0","TxnDate":"2026-11-20","DueDate":"2026-12-20","TotalAmt":10}}""");
        answer!.TxnDate.Should().Be(new DateOnly(2026, 11, 20));
        answer.DueDate.Should().Be(new DateOnly(2026, 12, 20));

        var bare = QuickBooksInvoiceAnswer.FromEntity("""{"Invoice":{"Id":"200","TxnDate":"soon"}}""");
        bare!.TxnDate.Should().BeNull();
        bare.DueDate.Should().BeNull();
    }
}
