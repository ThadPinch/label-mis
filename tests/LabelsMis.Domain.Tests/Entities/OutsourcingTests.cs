using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Jobs;
using LabelsMis.Domain.ValueObjects;

namespace LabelsMis.Domain.Tests.Entities;

public class OutsourcedItemTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly OutsourceDetails Details = new(Guid.NewGuid(), "  Q-100 ", new DateOnly(2026, 9, 1), " rush ");

    [Fact]
    public void CreateForLine_NormalizesDetailsAndStartsUntracked()
    {
        var item = OutsourcedItem.CreateForLine(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Details, 120m, UserId, Now);

        item.Should().Satisfy<OutsourcedItem>(i =>
        {
            i.IsLine.Should().BeTrue();
            i.QuoteNumber.Should().Be("Q-100");
            i.PrivateNotes.Should().Be("rush");
            i.VendorCost.Should().Be(120m);
            i.IsSent.Should().BeFalse();
            i.IsComplete.Should().BeFalse();
            i.QuantityReceived.Should().Be(0);
            i.CanBeRemoved.Should().BeTrue();
        });
    }

    [Fact]
    public void CreateForCharge_WithNegativeCost_Throws()
    {
        var act = () => OutsourcedItem.CreateForCharge(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Details, -1m, UserId, Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Receive_PartialThenBalance_CompletesWhenOrderedQuantityCovered()
    {
        var item = OutsourcedItem.CreateForLine(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Details, 120m, UserId, Now);

        item.Receive(Guid.NewGuid(), new DateOnly(2026, 8, 20), 600, "first box", markComplete: false, quantityOrdered: 1000, UserId, Now);
        item.IsComplete.Should().BeFalse("only 600 of 1,000 are in");
        item.CanBeRemoved.Should().BeFalse("a receipt exists");

        item.Receive(Guid.NewGuid(), new DateOnly(2026, 8, 22), 400, null, markComplete: false, quantityOrdered: 1000, UserId, Now);

        item.QuantityReceived.Should().Be(1000);
        item.IsComplete.Should().BeTrue();
        item.Receipts.Should().HaveCount(2);
    }

    [Fact]
    public void Receive_MarkCompleteWhenShort_CompletesAnyway()
    {
        var item = OutsourcedItem.CreateForLine(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Details, 120m, UserId, Now);

        item.Receive(Guid.NewGuid(), new DateOnly(2026, 8, 20), 950, "vendor short-shipped", markComplete: true, quantityOrdered: 1000, UserId, Now);

        item.IsComplete.Should().BeTrue();
        item.QuantityReceived.Should().Be(950);
    }

    [Fact]
    public void Receive_AfterComplete_Throws()
    {
        var item = OutsourcedItem.CreateForLine(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Details, 120m, UserId, Now);
        item.Receive(Guid.NewGuid(), new DateOnly(2026, 8, 20), 1000, null, false, 1000, UserId, Now);

        var act = () => item.Receive(Guid.NewGuid(), new DateOnly(2026, 8, 21), 1, null, false, 1000, UserId, Now);

        act.Should().Throw<InvalidOperationException>().WithMessage("*already been received*");
    }

    [Fact]
    public void Receive_ZeroQuantity_Throws()
    {
        var item = OutsourcedItem.CreateForLine(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Details, 120m, UserId, Now);

        var act = () => item.Receive(Guid.NewGuid(), new DateOnly(2026, 8, 20), 0, null, false, 1000, UserId, Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MarkSent_LocksRemovalAndRecordsDate()
    {
        var item = OutsourcedItem.CreateForLine(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Details, 120m, UserId, Now);
        var sentAt = new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc);

        item.MarkSent(sentAt, UserId, Now);

        item.SentToVendorAt.Should().Be(sentAt);
        item.CanBeRemoved.Should().BeFalse();
    }

    [Fact]
    public void MarkSent_AfterComplete_Throws()
    {
        var item = OutsourcedItem.CreateForLine(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Details, 120m, UserId, Now);
        item.MarkComplete(UserId, Now);

        var act = () => item.MarkSent(Now, UserId, Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void UpdateDetails_ReplacesVendorFactsWithoutTouchingTracking()
    {
        var item = OutsourcedItem.CreateForLine(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Details, 120m, UserId, Now);
        item.MarkSent(Now, UserId, Now);
        var newVendor = Guid.NewGuid();

        item.UpdateDetails(new OutsourceDetails(newVendor, "Q-200", new DateOnly(2026, 9, 15), null), 135m, UserId, Now);

        item.Should().Satisfy<OutsourcedItem>(i =>
        {
            i.VendorId.Should().Be(newVendor);
            i.QuoteNumber.Should().Be("Q-200");
            i.VendorCost.Should().Be(135m);
            i.ExpectedIn.Should().Be(new DateOnly(2026, 9, 15));
            i.PrivateNotes.Should().BeNull();
            i.IsSent.Should().BeTrue("tracking is untouched");
        });
    }
}

public class OutsourcedJobTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public void MarkOutsourced_OnFreshJob_RoutesToVendor()
    {
        var job = CreateJob();

        job.MarkOutsourced(UserId, Now);

        job.IsOutsourced.Should().BeTrue();
        job.Status.Should().Be(JobStatus.Outsourced);
    }

    [Fact]
    public void MarkOutsourced_WhenOperationsExist_Throws()
    {
        var job = CreateJob();
        job.AddOperation(JobOperation.Create(Guid.NewGuid(), job.Id, 1, JobOperationType.Press,
            EquipmentType.Press, Press.Indigo6800Id, 30, UserId, Now));

        var act = () => job.MarkOutsourced(UserId, Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ReceiveOutsourced_MovesStraightToReadyToShip()
    {
        var job = CreateJob();
        job.MarkOutsourced(UserId, Now);

        job.ReceiveOutsourced(UserId, Now);

        job.Status.Should().Be(JobStatus.Rewound);
        job.IsOutsourced.Should().BeTrue("the job remembers it was outsourced for costing and the ticket");
    }

    [Fact]
    public void ReceiveOutsourced_WhenNotAtVendor_Throws()
    {
        var job = CreateJob();

        var act = () => job.ReceiveOutsourced(UserId, Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AdvanceStatus_FromOutsourcedToShipped_IsForward()
    {
        // Outsourced sits below PrePress so the "only forward" guard still holds on receipt/shipment.
        var job = CreateJob();
        job.MarkOutsourced(UserId, Now);
        job.ReceiveOutsourced(UserId, Now);

        var act = () => job.AdvanceStatus(JobStatus.Shipped, UserId, Now);

        act.Should().NotThrow();
        job.Status.Should().Be(JobStatus.Shipped);
    }

    private static Job CreateJob() =>
        Job.CreatePlanned(Guid.NewGuid(), "JOB-2026-00002", Guid.NewGuid(), Guid.NewGuid(),
            1000, 1050, null, 5, null, null, UserId, Now);
}

public class OutsourcedEstimateTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public void ApplyOutsourcePricing_QuotesFinalPriceAndKeepsCalculatedFigures()
    {
        var brk = EstimateQuantityBreak.Create(Guid.NewGuid(), Guid.NewGuid(), 1000, 0.30m, 300m, 180m, 0.40m, null, "[]", UserId, Now);

        brk.ApplyOutsourcePricing(vendorCost: 120m, finalTotalPrice: 200m);

        brk.Should().Satisfy<EstimateQuantityBreak>(b =>
        {
            b.IsOutsourced.Should().BeTrue();
            b.OutsourceCost.Should().Be(120m);
            b.TotalPrice.Should().Be(200m);
            b.UnitPrice.Should().Be(0.20m);
            b.MarginPct.Should().Be(0.40m);
            b.CalculatedCost.Should().Be(180m);
            b.CalculatedUnitPrice.Should().Be(0.30m);
            b.CalculatedTotalPrice.Should().Be(300m);
        });
    }

    [Fact]
    public void ApplyOutsourcePricing_ZeroPrice_HasZeroMarginNotDivideByZero()
    {
        var brk = EstimateQuantityBreak.Create(Guid.NewGuid(), Guid.NewGuid(), 500, 0.30m, 150m, 90m, 0.40m, null, "[]", UserId, Now);

        brk.ApplyOutsourcePricing(50m, 0m);

        brk.MarginPct.Should().Be(0m);
        brk.UnitPrice.Should().Be(0m);
    }

    [Fact]
    public void ApplyOutsourcePricing_NegativeCost_Throws()
    {
        var brk = EstimateQuantityBreak.Create(Guid.NewGuid(), Guid.NewGuid(), 500, 0.30m, 150m, 90m, 0.40m, null, "[]", UserId, Now);

        var act = () => brk.ApplyOutsourcePricing(-5m, 100m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void EstimateCharge_SetOutsource_ThenClear_RoundTrips()
    {
        var charge = EstimateCharge.Create(Guid.NewGuid(), Guid.NewGuid(), 1, "500 promo pens", 500, 1.10m, UserId, Now);
        var vendor = Guid.NewGuid();

        charge.SetOutsource(new OutsourceDetails(vendor, "PP-7", null, "  "), 300m, UserId, Now);
        charge.Should().Satisfy<EstimateCharge>(c =>
        {
            c.IsOutsourced.Should().BeTrue();
            c.OutsourceVendorId.Should().Be(vendor);
            c.OutsourceCost.Should().Be(300m);
            c.OutsourcePrivateNotes.Should().BeNull("blank notes normalize to null");
            c.OutsourceDetails.Should().NotBeNull();
        });

        charge.SetOutsource(null, 999m, UserId, Now);
        charge.Should().Satisfy<EstimateCharge>(c =>
        {
            c.IsOutsourced.Should().BeFalse();
            c.OutsourceVendorId.Should().BeNull();
            c.OutsourceCost.Should().BeNull("clearing outsourcing drops the cost too");
            c.OutsourceDetails.Should().BeNull();
        });
    }

    [Fact]
    public void Supplier_SetOutsourceVendor_FlagsAndTrimsNotes()
    {
        var supplier = Supplier.Create(Guid.NewGuid(), "Promo Co", "PROMO", "Net 30", 5, null, UserId, Now);

        supplier.SetOutsourceVendor(true, "  pens, mugs  ", UserId, Now);

        supplier.IsOutsourceVendor.Should().BeTrue();
        supplier.OutsourceNotes.Should().Be("pens, mugs");
    }
}

public class OutsideCostTests
{
    [Fact]
    public void Calculate_WithOutsideCost_AddsItToTotal()
    {
        var result = JobCostCalculator.Calculate(
            [(1m, 100m)],
            [(10m, 0.5m)],
            outsideCost: 120m);

        result.TotalOutsideCost.Should().Be(120m);
        result.TotalCost.Should().Be(225m);
    }

    [Fact]
    public void Calculate_WithoutOutsideCost_IsUnchanged()
    {
        var result = JobCostCalculator.Calculate([(1m, 100m)], [(10m, 0.5m)]);

        result.TotalOutsideCost.Should().Be(0m);
        result.TotalCost.Should().Be(105m);
    }
}
