using LabelsMis.Domain.Entities;
using LabelsMis.Domain.QuickBooks;

namespace LabelsMis.Domain.Tests.Entities;

public class QuickBooksSyncLogTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Now = new(2026, 11, 10, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_KeepsWhatHappenedWhoAndWhen()
    {
        var invoiceId = Guid.NewGuid();

        var entry = QuickBooksSyncLog.Create(
            Guid.NewGuid(), QuickBooksOperations.TokenRefresh, true, Actor, automatic: true, Now,
            invoiceId: invoiceId, quickBooksId: "145", httpStatus: 200, intuitTid: " tid-1 ", durationMs: 312);

        entry.Should().Satisfy<QuickBooksSyncLog>(e =>
        {
            e.Operation.Should().Be("token.refresh");
            e.Succeeded.Should().BeTrue();
            e.Automatic.Should().BeTrue();
            e.CreatedById.Should().Be(Actor);
            e.CreatedAt.Should().Be(Now);
            e.InvoiceId.Should().Be(invoiceId);
            e.QuickBooksId.Should().Be("145");
            e.HttpStatus.Should().Be(200);
            e.IntuitTid.Should().Be("tid-1");
            e.DurationMs.Should().Be(312);
            e.Message.Should().BeNull();
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_WithoutAnOperation_Throws(string operation)
    {
        var act = () => QuickBooksSyncLog.Create(Guid.NewGuid(), operation, true, Actor, false, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_TruncatesALongMessage()
    {
        var entry = QuickBooksSyncLog.Create(
            Guid.NewGuid(), QuickBooksOperations.Connect, false, Actor, false, Now, message: new string('x', 5000));

        entry.Message.Should().HaveLength(QuickBooksSyncLog.MaxMessageLength);
    }

    [Fact]
    public void Create_BlankOptionalText_IsStoredAsNothing()
    {
        var entry = QuickBooksSyncLog.Create(
            Guid.NewGuid(), QuickBooksOperations.Connect, false, Actor, false, Now, errorCode: " ", message: "", intuitTid: "  ");

        entry.ErrorCode.Should().BeNull();
        entry.Message.Should().BeNull();
        entry.IntuitTid.Should().BeNull();
    }

    [Theory]
    [InlineData(QuickBooksOperations.Connect, "Connect to QuickBooks")]
    [InlineData(QuickBooksOperations.TokenRefresh, "Refresh the sign-in")]
    [InlineData("something.new", "something.new")]
    public void Describe_NamesKnownOperationsAndPassesOthersThrough(string operation, string expected)
    {
        QuickBooksOperations.Describe(operation).Should().Be(expected);
    }
}
