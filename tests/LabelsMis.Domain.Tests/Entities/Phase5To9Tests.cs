using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Inventory;
using LabelsMis.Domain.Jobs;

namespace LabelsMis.Domain.Tests.Entities;

public class JobTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public void Close_WhenOperationInProgress_Throws()
    {
        var job = CreateJob();
        var op = JobOperation.Create(Guid.NewGuid(), job.Id, 1, JobOperationType.Press,
            EquipmentType.Press, Press.Indigo6800Id, 30, UserId, Now);
        op.Start(UserId, Now, UserId, Now);
        job.AddOperation(op);

        var act = () => job.Close(UserId, Now);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*in progress*");
    }

    [Fact]
    public void JobCostCalculator_SumsLaborAndMaterial()
    {
        var result = JobCostCalculator.Calculate(
            [(2m, 150m), (1m, 100m)],
            [(500m, 0.05m)]);

        result.TotalLaborCost.Should().Be(400m);
        result.TotalMaterialCost.Should().Be(25m);
        result.TotalCost.Should().Be(425m);
    }

    private static Job CreateJob() =>
        Job.CreatePlanned(Guid.NewGuid(), "JOB-2026-00001", Guid.NewGuid(), Guid.NewGuid(),
            5000, 5250, null, 5, null, null, UserId, Now);
}

public class RollTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public void Consume_ExceedingRemaining_Throws()
    {
        var roll = Roll.Create(Guid.NewGuid(), "R-2026-00001-A", Guid.NewGuid(), "LOT1",
            13m, 1000m, Now, null, null, UserId, Now);

        var act = () => roll.Consume(1001m, UserId, Now);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*remaining length*");
    }

    [Fact]
    public void SplitValidator_WidthMismatch_Throws()
    {
        var act = () => RollSplitValidator.ValidateWidths(13m, [6m, 6m]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Reconciliation_BalancedWhenReceivedEqualsRemainingPlusConsumed()
    {
        var result = RollReconciliation.Calculate(
            Guid.NewGuid(), totalReceivedLf: 10000m, totalRemainingLf: 3000m, totalConsumedLf: 7000m);

        result.IsBalanced.Should().BeTrue();
    }
}

public class ShipmentTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public void MarkInTransit_WithoutPackages_Throws()
    {
        var shipment = Shipment.CreatePending(
            Guid.NewGuid(), "SHP-2026-00001", Guid.NewGuid(), DateOnly.FromDateTime(Now),
            Carrier.Fedex, FedexServiceLevel.FedexGround, Guid.NewGuid(),
            LabelsMis.Domain.ValueObjects.ShippingAddress.Empty,
            LabelsMis.Domain.ValueObjects.ShippingAddress.Empty, Guid.NewGuid(),
            100m, BillingType.Sender, null, UserId, Now);

        var act = () => shipment.MarkInTransit(10m, UserId, Now);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*without packages*");
    }

    [Fact]
    public void MarkShippedWithoutPackages_ShipsPendingShipment_AndRejectsRepeat()
    {
        var shipment = Shipment.CreatePending(
            Guid.NewGuid(), "SHP-2026-00002", Guid.NewGuid(), DateOnly.FromDateTime(Now),
            Carrier.Other, null, null,
            LabelsMis.Domain.ValueObjects.ShippingAddress.Empty,
            LabelsMis.Domain.ValueObjects.ShippingAddress.Empty, null,
            0m, BillingType.Sender, null, UserId, Now);

        shipment.MarkShippedWithoutPackages(UserId, Now);

        shipment.Status.Should().Be(ShipmentStatus.InTransit);
        shipment.TotalShippingCost.Should().Be(0m);

        var act = () => shipment.MarkShippedWithoutPackages(UserId, Now);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*pending*");
    }
}

public class InvoiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public void Void_WhenPaid_Throws()
    {
        var invoice = Invoice.CreateDraft(
            Guid.NewGuid(), "INV-2026-00001", Guid.NewGuid(), Guid.NewGuid(), null,
            DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now.AddDays(30)),
            100m, 0m, 0m, null, UserId, Now);
        invoice.AddPayment(Payment.Create(Guid.NewGuid(), invoice.Id, DateOnly.FromDateTime(Now),
            100m, PaymentMethod.Check, null, null, UserId, Now));

        var act = () => invoice.Void("mistake", UserId, Now);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Paid*");
    }

    [Fact]
    public void MarkExported_RecordsTimestampAndUser_AndUnmarkClearsBoth()
    {
        var invoice = Invoice.CreateDraft(
            Guid.NewGuid(), "INV-2026-00003", Guid.NewGuid(), Guid.NewGuid(), null,
            DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now.AddDays(30)),
            100m, 0m, 0m, null, UserId, Now);

        invoice.MarkExported(Now, UserId, Now);

        invoice.QbExportedAt.Should().Be(Now);
        invoice.QbExportedById.Should().Be(UserId);

        invoice.UnmarkExported(UserId, Now);

        invoice.QbExportedAt.Should().BeNull();
        invoice.QbExportedById.Should().BeNull();
    }

    [Fact]
    public void MarkExported_WhenVoid_Throws()
    {
        var invoice = Invoice.CreateDraft(
            Guid.NewGuid(), "INV-2026-00004", Guid.NewGuid(), Guid.NewGuid(), null,
            DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now.AddDays(30)),
            100m, 0m, 0m, null, UserId, Now);
        invoice.Void("duplicate", UserId, Now);

        var act = () => invoice.MarkExported(Now, UserId, Now);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Void*");
    }

    [Fact]
    public void RecordPayment_ExceedingBalance_Throws()
    {
        var invoice = Invoice.CreateDraft(
            Guid.NewGuid(), "INV-2026-00002", Guid.NewGuid(), Guid.NewGuid(), null,
            DateOnly.FromDateTime(Now), DateOnly.FromDateTime(Now.AddDays(30)),
            100m, 0m, 0m, null, UserId, Now);

        var act = () => invoice.RecordPayment(Payment.Create(Guid.NewGuid(), invoice.Id,
            DateOnly.FromDateTime(Now), 150m, PaymentMethod.Check, null, null, UserId, Now));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*balance due*");
    }
}
