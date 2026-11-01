using LabelsMis.Domain.Entities;

namespace LabelsMis.Domain.Tests.Entities;

public class InvoiceNonBillableTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void MarkNonBillable_RecordsReasonWhoAndWhen_AndBlocksExport()
    {
        var invoice = Draft(0m);

        invoice.MarkNonBillable("  Free sample for Acme  ", UserId, Now);

        invoice.NonBillable.Should().BeTrue();
        invoice.NonBillableReason.Should().Be("Free sample for Acme");
        invoice.NonBillableAt.Should().Be(Now);
        invoice.NonBillableById.Should().Be(UserId);
        ((Action)(() => invoice.MarkExported(Now, UserId, Now))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkNonBillable_RequiresAReason()
    {
        ((Action)(() => Draft(0m).MarkNonBillable(" ", UserId, Now))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MarkNonBillable_RefusedOnceExportedOrPaidOrVoid()
    {
        var exported = Draft(100m);
        exported.MarkExported(Now, UserId, Now);
        var paid = Draft(100m);
        paid.RecordPayment(Payment.Create(Guid.NewGuid(), paid.Id, DateOnly.FromDateTime(Now), 40m,
            Enums.PaymentMethod.Check, null, null, UserId, Now));
        var voided = Draft(100m);
        voided.Void("dup", UserId, Now);

        ((Action)(() => exported.MarkNonBillable("x", UserId, Now))).Should().Throw<InvalidOperationException>();
        ((Action)(() => paid.MarkNonBillable("x", UserId, Now))).Should().Throw<InvalidOperationException>();
        ((Action)(() => voided.MarkNonBillable("x", UserId, Now))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ClearNonBillable_Reopens()
    {
        var invoice = Draft(0m);
        invoice.MarkNonBillable("sample", UserId, Now);

        invoice.ClearNonBillable(UserId, Now);

        invoice.NonBillable.Should().BeFalse();
        invoice.NonBillableReason.Should().BeNull();
        invoice.NonBillableAt.Should().BeNull();
    }

    private static Invoice Draft(decimal subtotal) => Invoice.CreateDraft(
        Guid.NewGuid(), "INV-T-2", Guid.NewGuid(), Guid.NewGuid(), null,
        new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 1), subtotal, 0m, 0m, null, UserId, Now);
}
