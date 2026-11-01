using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Shipping;

namespace LabelsMis.Domain.Tests.Shipping;

public class InvoiceShippingRulesTests
{
    [Fact]
    public void Resolve_NothingShipped_UsesTheQuote()
    {
        var r = InvoiceShippingRules.Resolve(null, quotedShipping: 35m, currentAmount: 35m, []);

        r.Amount.Should().Be(35m);
        r.Source.Should().Be(InvoiceShippingSource.Quote);
    }

    [Fact]
    public void Resolve_Shipped_SumsBilledShipping()
    {
        var r = InvoiceShippingRules.Resolve(null, 35m, 35m,
            [new ShippedShipmentBilling("SHP-1", 30m), new ShippedShipmentBilling("SHP-2", 15m)]);

        r.Amount.Should().Be(45m);
        r.Source.Should().Be(InvoiceShippingSource.Shipments);
        r.IsPending.Should().BeFalse();
    }

    [Fact]
    public void Resolve_AShipmentWithoutBilledShipping_KeepsTheCurrentAmountAndIsPending()
    {
        var r = InvoiceShippingRules.Resolve(null, 35m, currentAmount: 30m,
            [new ShippedShipmentBilling("SHP-1", 30m), new ShippedShipmentBilling("SHP-2", null)]);

        r.Amount.Should().Be(30m);
        r.IsPending.Should().BeTrue();
        r.PendingShipmentNumbers.Should().Equal("SHP-2");
    }

    [Fact]
    public void Resolve_ManualOverride_Wins()
    {
        var r = InvoiceShippingRules.Resolve(12m, 35m, 30m, [new ShippedShipmentBilling("SHP-1", null)]);

        r.Amount.Should().Be(12m);
        r.Source.Should().Be(InvoiceShippingSource.Manual);
        r.IsPending.Should().BeFalse();
    }

    [Fact]
    public void Invoice_ApplyShipping_MovesTotalAndBalanceByTheDifference()
    {
        var invoice = Draft(subtotal: 500m, tax: 41.25m, shipping: 35m);

        invoice.ApplyShipping(30m, Guid.NewGuid(), DateTime.UtcNow);

        invoice.ShippingAmount.Should().Be(30m);
        invoice.Total.Should().Be(571.25m);
        invoice.BalanceDue.Should().Be(571.25m);
    }

    [Fact]
    public void Invoice_SentOrExported_IsNeverRewritten()
    {
        var sent = Draft(500m, 0m, 35m);
        sent.MarkSent(null, Guid.NewGuid(), DateTime.UtcNow);
        var exported = Draft(500m, 0m, 35m);
        exported.MarkExported(DateTime.UtcNow, Guid.NewGuid(), DateTime.UtcNow);

        ((Action)(() => sent.ApplyShipping(30m, Guid.NewGuid(), DateTime.UtcNow))).Should().Throw<InvalidOperationException>();
        ((Action)(() => exported.ApplyShipping(30m, Guid.NewGuid(), DateTime.UtcNow))).Should().Throw<InvalidOperationException>();
        ((Action)(() => exported.SetShippingOverride(10m, Guid.NewGuid(), DateTime.UtcNow))).Should().Throw<InvalidOperationException>();
        sent.ShippingAmount.Should().Be(35m);
    }

    [Fact]
    public void Invoice_SetShippingOverride_RejectsNegative_AndClears()
    {
        var invoice = Draft(500m, 0m, 35m);

        ((Action)(() => invoice.SetShippingOverride(-1m, Guid.NewGuid(), DateTime.UtcNow)))
            .Should().Throw<ArgumentOutOfRangeException>();
        invoice.SetShippingOverride(12m, Guid.NewGuid(), DateTime.UtcNow);
        invoice.ShippingOverride.Should().Be(12m);
        invoice.SetShippingOverride(null, Guid.NewGuid(), DateTime.UtcNow);
        invoice.ShippingOverride.Should().BeNull();
    }

    private static Invoice Draft(decimal subtotal, decimal tax, decimal shipping) => Invoice.CreateDraft(
        Guid.NewGuid(), "INV-T-1", Guid.NewGuid(), Guid.NewGuid(), null,
        new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 1), subtotal, tax, shipping, null, Guid.NewGuid(), DateTime.UtcNow);
}
