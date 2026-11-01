using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Invoicing;

namespace LabelsMis.Domain.Tests.Invoicing;

public class InvoiceReadinessRulesTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void WaitingReasons_AllLinesShipped_TotalPositive_IsReady()
    {
        InvoiceReadinessRules.WaitingReasons(Facts(shippedLines: 2, totalLines: 2)).Should().BeEmpty();
    }

    [Fact]
    public void WaitingReasons_NothingShipped_ListsLineProgressWithoutUnderRunHint()
    {
        InvoiceReadinessRules.WaitingReasons(Facts(shippedLines: 0, totalLines: 2))
            .Should().Equal("Not shipped yet (0 of 2 lines)");
    }

    [Fact]
    public void WaitingReasons_PartlyShipped_WaitsAndPointsAtMarkOrderShipped()
    {
        var reasons = InvoiceReadinessRules.WaitingReasons(Facts(shippedLines: 1, totalLines: 2, anyShipped: true));

        reasons.Should().ContainSingle()
            .Which.Should().Be($"Not shipped yet (1 of 2 lines). {InvoiceReadinessRules.UnderRunHint}");
    }

    [Fact]
    public void WaitingReasons_UnderRunOrderMarkedShipped_IsReady()
    {
        InvoiceReadinessRules.WaitingReasons(Facts(shippedLines: 0, totalLines: 1, anyShipped: true, orderMarkedShipped: true))
            .Should().BeEmpty();
    }

    [Fact]
    public void WaitingReasons_BillBeforeShip_SkipsTheShippingTest()
    {
        InvoiceReadinessRules.WaitingReasons(Facts(shippedLines: 0, totalLines: 3, billBeforeShip: true))
            .Should().BeEmpty();
    }

    [Fact]
    public void WaitingReasons_ChargeOnlyOrder_NeedsBillBeforeShip()
    {
        InvoiceReadinessRules.WaitingReasons(Facts(shippedLines: 0, totalLines: 0))
            .Should().Equal("Nothing on the order ships — use Bill before shipping");
        InvoiceReadinessRules.WaitingReasons(Facts(shippedLines: 0, totalLines: 0, billBeforeShip: true))
            .Should().BeEmpty();
    }

    [Fact]
    public void WaitingReasons_ZeroTotal_PendingCarrierCostAndCustomerBlockers_AreAllListed()
    {
        var reasons = InvoiceReadinessRules.WaitingReasons(Facts(
            shippedLines: 1, totalLines: 1, total: 0m,
            pending: ["SHP-1", "SHP-2"], customerBlockers: ["Waiting on new-client form"]));

        reasons.Should().Equal(
            "$0 total — close it as non-billable or add charges",
            "Waiting on carrier cost (SHP-1, SHP-2)",
            "Waiting on new-client form");
    }

    [Fact]
    public void SetBillBeforeShip_SetsFlag_AndIsRefusedOnVoidOrExported()
    {
        var invoice = Draft();
        invoice.SetBillBeforeShip(true, UserId, Now);
        invoice.BillBeforeShip.Should().BeTrue();
        invoice.SetBillBeforeShip(false, UserId, Now);
        invoice.BillBeforeShip.Should().BeFalse();

        var exported = Draft();
        exported.MarkExported(Now, UserId, Now);
        var voided = Draft();
        voided.Void("dup", UserId, Now);

        ((Action)(() => exported.SetBillBeforeShip(true, UserId, Now))).Should().Throw<InvalidOperationException>();
        ((Action)(() => voided.SetBillBeforeShip(true, UserId, Now))).Should().Throw<InvalidOperationException>();
    }

    private static InvoiceReadinessFacts Facts(
        int shippedLines,
        int totalLines,
        bool anyShipped = false,
        bool orderMarkedShipped = false,
        bool billBeforeShip = false,
        decimal total = 100m,
        IReadOnlyList<string>? pending = null,
        IReadOnlyList<string>? customerBlockers = null) =>
        new(billBeforeShip, orderMarkedShipped, shippedLines, totalLines, anyShipped, total,
            pending ?? [], customerBlockers ?? []);

    private static Invoice Draft() => Invoice.CreateDraft(
        Guid.NewGuid(), "INV-T-6", Guid.NewGuid(), Guid.NewGuid(), null,
        new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 1), 100m, 0m, 0m, null, UserId, Now);
}
