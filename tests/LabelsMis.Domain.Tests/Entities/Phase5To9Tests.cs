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
    public void ActualHours_FallsBackToClockedEntries_WhenNoMinutesRecorded()
    {
        var op = JobOperation.Create(Guid.NewGuid(), Guid.NewGuid(), 1, JobOperationType.Press,
            EquipmentType.Press, Press.Indigo6800Id, 30, UserId, Now);
        op.ClockOn(Guid.NewGuid(), UserId, Now);
        op.ClockOff(UserId, Now.AddMinutes(90), 100, 5, 0, null, UserId, Now.AddMinutes(90));

        op.ActualHours.Should().Be(1.5m);
    }

    [Fact]
    public void ActualHours_PrefersRecordedMinutes_OverClockedEntries()
    {
        var op = JobOperation.Create(Guid.NewGuid(), Guid.NewGuid(), 1, JobOperationType.Press,
            EquipmentType.Press, Press.Indigo6800Id, 30, UserId, Now);
        op.ClockOn(Guid.NewGuid(), UserId, Now);
        op.ClockOff(UserId, Now.AddMinutes(90), 100, 5, 0, null, UserId, Now.AddMinutes(90));

        // The operator states the step really took 15 minutes — under plan (a favorable
        // variance); the stated time replaces the clocked time rather than adding to it.
        op.RecordActualMinutes(15m, UserId, Now.AddMinutes(91));

        op.ActualHours.Should().Be(0.25m);
    }

    [Fact]
    public void SetCounts_RecordsActualMinutes_AsAbsoluteValue()
    {
        var op = JobOperation.Create(Guid.NewGuid(), Guid.NewGuid(), 1, JobOperationType.Press,
            EquipmentType.Press, Press.Indigo6800Id, 30, UserId, Now);

        op.SetCounts(100, 5, 10, DowntimeReasonCode.Setup, 20m, UserId, Now);
        op.SetCounts(100, 5, 10, DowntimeReasonCode.Setup, 25m, UserId, Now);

        op.ActualMinutes.Should().Be(25m);
        op.ActualHours.Should().Be(25m / 60m);
    }

    [Fact]
    public void RecordActualMinutes_Negative_Throws()
    {
        var op = JobOperation.Create(Guid.NewGuid(), Guid.NewGuid(), 1, JobOperationType.Press,
            EquipmentType.Press, Press.Indigo6800Id, 30, UserId, Now);

        var act = () => op.RecordActualMinutes(-1m, UserId, Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
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

    [Fact]
    public void AddOperationBeforeClosingSteps_WithClosingSteps_InsertsAheadOfInspectionAndRenumbers()
    {
        var job = CreateJob();
        var press = AddOperation(job, 1, JobOperationType.Press);
        var lam = AddOperation(job, 2, JobOperationType.Finishing);
        var inspect = AddOperation(job, 3, JobOperationType.Inspection);
        var pack = AddOperation(job, 4, JobOperationType.Pack);
        var ship = AddOperation(job, 5, JobOperationType.Ship);
        var dieCut = CreateOperation(job.Id, 99, JobOperationType.Finishing);

        job.AddOperationBeforeClosingSteps(dieCut, UserId, Now);

        job.Operations.OrderBy(o => o.Sequence).Should().Equal(press, lam, dieCut, inspect, pack, ship);
        job.Operations.Select(o => o.Sequence).OrderBy(s => s).Should().Equal(1, 2, 3, 4, 5, 6);
    }

    [Fact]
    public void AddOperationBeforeClosingSteps_WithoutClosingSteps_AppendsAtEnd()
    {
        var job = CreateJob();
        var press = AddOperation(job, 1, JobOperationType.Press);
        var added = CreateOperation(job.Id, 7, JobOperationType.Finishing);

        job.AddOperationBeforeClosingSteps(added, UserId, Now);

        job.Operations.OrderBy(o => o.Sequence).Should().Equal(press, added);
        added.Sequence.Should().Be(2);
    }

    [Fact]
    public void AddOperationBeforeClosingSteps_WhenEarlierStepStarted_KeepsStartedStepSequence()
    {
        var job = CreateJob();
        var press = AddOperation(job, 1, JobOperationType.Press);
        press.Start(UserId, Now, UserId, Now);
        var inspect = AddOperation(job, 2, JobOperationType.Inspection);
        var added = CreateOperation(job.Id, 3, JobOperationType.Finishing);

        job.AddOperationBeforeClosingSteps(added, UserId, Now);

        press.Sequence.Should().Be(1);
        added.Sequence.Should().Be(2);
        inspect.Sequence.Should().Be(3);
    }

    [Fact]
    public void AddOperationBeforeClosingSteps_WhenOperationNotPending_Throws()
    {
        var job = CreateJob();
        var started = CreateOperation(job.Id, 1, JobOperationType.Finishing);
        started.Start(UserId, Now, UserId, Now);

        var act = () => job.AddOperationBeforeClosingSteps(started, UserId, Now);

        act.Should().Throw<InvalidOperationException>().WithMessage("*pending*");
    }

    [Fact]
    public void RemoveOperation_WhenPending_RemovesAndClosesSequenceGap()
    {
        var job = CreateJob();
        var press = AddOperation(job, 1, JobOperationType.Press);
        var lam = AddOperation(job, 2, JobOperationType.Finishing);
        var inspect = AddOperation(job, 3, JobOperationType.Inspection);
        var ship = AddOperation(job, 4, JobOperationType.Ship);

        job.RemoveOperation(lam, UserId, Now);

        job.Operations.OrderBy(o => o.Sequence).Should().Equal(press, inspect, ship);
        job.Operations.Select(o => o.Sequence).OrderBy(s => s).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void RemoveOperation_WhenStarted_Throws()
    {
        var job = CreateJob();
        var lam = AddOperation(job, 1, JobOperationType.Finishing);
        lam.Start(UserId, Now, UserId, Now);

        var act = () => job.RemoveOperation(lam, UserId, Now);

        act.Should().Throw<InvalidOperationException>().WithMessage("*pending*");
        job.Operations.Should().Contain(lam);
    }

    [Fact]
    public void RemoveOperation_WhenNotOnJob_Throws()
    {
        var job = CreateJob();
        var stray = CreateOperation(Guid.NewGuid(), 1, JobOperationType.Finishing);

        var act = () => job.RemoveOperation(stray, UserId, Now);

        act.Should().Throw<InvalidOperationException>().WithMessage("*does not belong*");
    }

    [Fact]
    public void RemoveOperation_WhenLaterStepCompleted_LeavesCompletedSequenceAlone()
    {
        var job = CreateJob();
        var press = AddOperation(job, 1, JobOperationType.Press);
        var lam = AddOperation(job, 2, JobOperationType.Finishing);
        var inspect = AddOperation(job, 3, JobOperationType.Inspection);
        inspect.Complete(UserId, Now);
        var ship = AddOperation(job, 4, JobOperationType.Ship);

        job.RemoveOperation(lam, UserId, Now);

        press.Sequence.Should().Be(1);
        inspect.Sequence.Should().Be(3);
        ship.Sequence.Should().Be(4);
    }

    [Fact]
    public void Resequence_WhenPending_UpdatesSequence()
    {
        var op = CreateOperation(Guid.NewGuid(), 1, JobOperationType.Finishing);

        op.Resequence(4, UserId, Now);

        op.Sequence.Should().Be(4);
    }

    [Fact]
    public void Resequence_WhenCompleted_Throws()
    {
        var op = CreateOperation(Guid.NewGuid(), 1, JobOperationType.Finishing);
        op.Complete(UserId, Now);

        var act = () => op.Resequence(2, UserId, Now);

        act.Should().Throw<InvalidOperationException>().WithMessage("*pending*");
    }

    [Fact]
    public void Resequence_WhenZero_Throws()
    {
        var op = CreateOperation(Guid.NewGuid(), 1, JobOperationType.Finishing);

        var act = () => op.Resequence(0, UserId, Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static Job CreateJob() =>
        Job.CreatePlanned(Guid.NewGuid(), "JOB-2026-00001", Guid.NewGuid(), Guid.NewGuid(),
            5000, 5250, null, 5, null, null, UserId, Now);

    private static JobOperation CreateOperation(Guid jobId, int sequence, JobOperationType type) =>
        JobOperation.Create(
            Guid.NewGuid(),
            jobId,
            sequence,
            type,
            type switch
            {
                JobOperationType.Press => EquipmentType.Press,
                JobOperationType.Finishing => EquipmentType.FinishingOperation,
                _ => EquipmentType.None
            },
            type == JobOperationType.Finishing ? Guid.NewGuid() : null,
            10,
            UserId,
            Now);

    private static JobOperation AddOperation(Job job, int sequence, JobOperationType type)
    {
        var operation = CreateOperation(job.Id, sequence, type);
        job.AddOperation(operation);
        return operation;
    }
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
