using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Jobs;

namespace LabelsMis.Domain.Tests.Jobs;

public class JobFloorLocatorTests
{
    private static readonly DateOnly Today = new(2026, 11, 5);

    private static List<FloorOperation> Route(params (JobOperationType Type, JobOperationStatus Status, string Label)[] ops) =>
        ops.Select((o, i) => new FloorOperation(i + 1, o.Type, o.Status, o.Label)).ToList();

    private static List<FloorOperation> StandardRoute(
        JobOperationStatus press = JobOperationStatus.Pending,
        JobOperationStatus laminate = JobOperationStatus.Pending,
        JobOperationStatus dieCut = JobOperationStatus.Pending,
        JobOperationStatus qc = JobOperationStatus.Pending) =>
        Route(
            (JobOperationType.Press, press, "Press"),
            (JobOperationType.Finishing, laminate, "Laminate"),
            (JobOperationType.Finishing, dieCut, "Die cut"),
            (JobOperationType.Inspection, qc, "QC inspection"),
            (JobOperationType.Pack, JobOperationStatus.Pending, "Pack"),
            (JobOperationType.Ship, JobOperationStatus.Pending, "Ship"));

    [Fact]
    public void Locate_PrePressNotImposed_NeedsImpositionAndPrePressIsCurrent()
    {
        var location = JobFloorLocator.Locate(new JobFloorFacts(JobStatus.PrePress, false, StandardRoute()), Today);

        location.Should().Satisfy<JobFloorLocation>(l =>
        {
            l.StageLabel.Should().Be("Pre-press");
            l.Where.Should().Be("Needs imposition");
            l.Route[0].Should().Be(new FloorStep("Pre-press", FloorStepState.Current));
            l.Route.Skip(1).Should().OnlyContain(s => s.State == FloorStepState.Upcoming);
        });
    }

    [Fact]
    public void Locate_PrePressWithStaleImposition_FlagsAttention()
    {
        var facts = new JobFloorFacts(JobStatus.PrePress, false, StandardRoute(), IsImposed: true, ImposedIsStale: true);

        var location = JobFloorLocator.Locate(facts, Today);

        location.Should().Satisfy<JobFloorLocation>(l =>
        {
            l.Where.Should().Be("Re-run imposition");
            l.NeedsAttention.Should().BeTrue();
        });
    }

    [Fact]
    public void Locate_Queued_NamesThePressAndMarksThePressStepCurrent()
    {
        var facts = new JobFloorFacts(JobStatus.Queued, false, StandardRoute(), PressName: "HP Indigo 6800");

        var location = JobFloorLocator.Locate(facts, Today);

        location.Should().Satisfy<JobFloorLocation>(l =>
        {
            l.StageLabel.Should().Be("Press");
            l.Where.Should().Be("Queued for HP Indigo 6800");
            l.IsRunning.Should().BeFalse();
            l.Route[0].State.Should().Be(FloorStepState.Done);
            l.Route[1].Should().Be(new FloorStep("Press — HP Indigo 6800", FloorStepState.Current));
        });
    }

    [Fact]
    public void Locate_PressClockedOn_IsRunningAndNamesTheOperator()
    {
        var operations = StandardRoute(press: JobOperationStatus.InProgress);
        operations[0] = operations[0] with { ClockedOnBy = "tom" };
        var facts = new JobFloorFacts(JobStatus.Queued, false, operations, PressName: "HP Indigo 6800");

        var location = JobFloorLocator.Locate(facts, Today);

        location.Should().Satisfy<JobFloorLocation>(l =>
        {
            l.Where.Should().Be("Printing on HP Indigo 6800");
            l.IsRunning.Should().BeTrue();
            l.RunningBy.Should().Be("tom");
            l.Route[1].Running.Should().BeTrue();
        });
    }

    [Fact]
    public void Locate_FinishingWithFirstStepDone_IsOnTheSecondStep()
    {
        var facts = new JobFloorFacts(
            JobStatus.Printed, false,
            StandardRoute(press: JobOperationStatus.Complete, laminate: JobOperationStatus.Complete));

        var location = JobFloorLocator.Locate(facts, Today);

        location.Should().Satisfy<JobFloorLocation>(l =>
        {
            l.StageLabel.Should().Be("Finishing");
            l.Where.Should().Be("Die cut");
            l.Detail.Should().Be("Step 2 of 2");
            l.StepsDone.Should().Be(3);
            l.Route.Single(s => s.State == FloorStepState.Current).Label.Should().Be("Die cut");
        });
    }

    [Fact]
    public void Locate_FinishingWithOneStep_HasNoStepCount()
    {
        var operations = Route(
            (JobOperationType.Press, JobOperationStatus.Complete, "Press"),
            (JobOperationType.Finishing, JobOperationStatus.Pending, "Laminate"),
            (JobOperationType.Inspection, JobOperationStatus.Pending, "QC inspection"));

        var location = JobFloorLocator.Locate(new JobFloorFacts(JobStatus.Printed, false, operations), Today);

        location.Should().Satisfy<JobFloorLocation>(l =>
        {
            l.Where.Should().Be("Laminate");
            l.Detail.Should().BeNull();
        });
    }

    [Fact]
    public void Locate_FinishingWithNoFinishingSteps_SaysSo()
    {
        var operations = Route(
            (JobOperationType.Press, JobOperationStatus.Complete, "Press"),
            (JobOperationType.Inspection, JobOperationStatus.Pending, "QC inspection"));

        var location = JobFloorLocator.Locate(new JobFloorFacts(JobStatus.Printed, false, operations), Today);

        location.Where.Should().Be("Ready for finishing");
    }

    [Fact]
    public void Locate_Rewinding_IsRewindAndQc()
    {
        var facts = new JobFloorFacts(
            JobStatus.Finished, false,
            StandardRoute(JobOperationStatus.Complete, JobOperationStatus.Complete, JobOperationStatus.Complete));

        var location = JobFloorLocator.Locate(facts, Today);

        location.Should().Satisfy<JobFloorLocation>(l =>
        {
            l.StageLabel.Should().Be("Rewinding");
            l.Where.Should().Be("Rewind & QC");
            l.Route.Single(s => s.State == FloorStepState.Current).Label.Should().Be("QC inspection");
        });
    }

    [Theory]
    [InlineData("Pickup", false, "Pickup")]
    [InlineData("FedEx", true, "FedEx · record the shipment")]
    [InlineData(null, true, "No shipping method · record the shipment")]
    public void Locate_Rewound_IsReadyToShipWithTheMethod(string? method, bool recordRequired, string detail)
    {
        var facts = new JobFloorFacts(
            JobStatus.Rewound, false,
            StandardRoute(JobOperationStatus.Complete, JobOperationStatus.Complete, JobOperationStatus.Complete, JobOperationStatus.Complete),
            ShippingMethodName: method,
            ShipmentRecordRequired: recordRequired);

        var location = JobFloorLocator.Locate(facts, Today);

        location.Should().Satisfy<JobFloorLocation>(l =>
        {
            l.StageLabel.Should().Be("Shipping");
            l.Where.Should().Be("Ready to ship");
            l.Detail.Should().Be(detail);
            l.Route.Single(s => s.State == FloorStepState.Current).Label.Should().Be("Pack");
        });
    }

    [Fact]
    public void Locate_OutsourcedPastExpectedIn_IsLateAtTheVendor()
    {
        var operations = Route(
            (JobOperationType.Inspection, JobOperationStatus.Pending, "QC inspection"),
            (JobOperationType.Pack, JobOperationStatus.Pending, "Pack"),
            (JobOperationType.Ship, JobOperationStatus.Pending, "Ship"));
        var facts = new JobFloorFacts(
            JobStatus.Outsourced, true, operations, VendorName: "Acme Flexo", VendorExpectedIn: Today.AddDays(-1));

        var location = JobFloorLocator.Locate(facts, Today);

        location.Should().Satisfy<JobFloorLocation>(l =>
        {
            l.Where.Should().Be("At Acme Flexo");
            l.Detail.Should().Be("Expected Nov 4 — late");
            l.NeedsAttention.Should().BeTrue();
            l.Route[0].Should().Be(new FloorStep("Vendor — Acme Flexo", FloorStepState.Current));
        });
    }

    [Fact]
    public void Locate_OutsourcedDueLater_IsNotLate()
    {
        var facts = new JobFloorFacts(JobStatus.Outsourced, true, [], VendorExpectedIn: Today);

        var location = JobFloorLocator.Locate(facts, Today);

        location.Should().Satisfy<JobFloorLocation>(l =>
        {
            l.Where.Should().Be("At the vendor");
            l.Detail.Should().Be("Expected Nov 5");
            l.NeedsAttention.Should().BeFalse();
        });
    }

    [Fact]
    public void Locate_Shipped_HasNoCurrentStep()
    {
        var facts = new JobFloorFacts(
            JobStatus.Shipped, false,
            StandardRoute(JobOperationStatus.Complete, JobOperationStatus.Complete, JobOperationStatus.Complete, JobOperationStatus.Complete));

        var location = JobFloorLocator.Locate(facts, Today);

        location.Should().Satisfy<JobFloorLocation>(l =>
        {
            l.Where.Should().Be("Shipped");
            l.Route.Should().NotContain(s => s.State == FloorStepState.Current);
        });
    }

    [Fact]
    public void Locate_OperationsOutOfSequenceOrder_AreRouteOrdered()
    {
        var operations = new List<FloorOperation>
        {
            new(3, JobOperationType.Inspection, JobOperationStatus.Pending, "QC inspection"),
            new(1, JobOperationType.Press, JobOperationStatus.Complete, "Press"),
            new(2, JobOperationType.Finishing, JobOperationStatus.Pending, "Laminate"),
        };

        var location = JobFloorLocator.Locate(new JobFloorFacts(JobStatus.Printed, false, operations), Today);

        location.Route.Select(s => s.Label).Should().Equal("Pre-press", "Press", "Laminate", "QC inspection");
    }
}
