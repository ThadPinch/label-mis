using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Estimating;
using LabelsMis.Domain.ValueObjects;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.Estimates;
using LabelsMis.Web.Services.Jobs;
using LabelsMis.Web.Services.Outsourcing;
using LabelsMis.Web.Services.Rolls;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Tests;

/// <summary>
/// The outsourcing flow end to end at the service layer: an outsourced order line is released as a
/// vendor-routed job, receipts accumulate on the production Outsourced list, and the job lands in
/// ready-to-ship on the final receipt. Requires the PostgreSQL test database (same convention as
/// <see cref="RollServiceIntegrationTests"/>).
/// </summary>
public class OutsourcingIntegrationTests : IAsyncLifetime
{
    private static readonly Guid TestUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private LabelsMisDbContext _db = null!;
    private JobService _jobs = null!;
    private OutsourceService _outsourcing = null!;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5432;Database=labels_mis_test;Username=labels_mis;Password=test_password";
        var options = new DbContextOptionsBuilder<LabelsMisDbContext>().UseNpgsql(connectionString).Options;
        _db = new LabelsMisDbContext(options);
        await _db.Database.MigrateAsync();

        if (!await _db.Users.AnyAsync(u => u.Id == TestUserId))
        {
            _db.Users.Add(new Infrastructure.Identity.ApplicationUser
            {
                Id = TestUserId,
                UserName = "test@labels-mis.local",
                Email = "test@labels-mis.local",
                EmailConfirmed = true,
                NormalizedEmail = "TEST@LABELS-MIS.LOCAL",
                NormalizedUserName = "TEST@LABELS-MIS.LOCAL"
            });
            await _db.SaveChangesAsync();
        }

        var user = new StubCurrentUserService(TestUserId);
        var numbers = new DocumentNumberService(_db);
        _jobs = new JobService(_db, user, numbers, new EstimateCalculationMapper(_db), new EstimatingService(), new RollService(_db, user, numbers));
        _outsourcing = new OutsourceService(_db, user, _jobs);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task ReleaseAndReceive_OutsourcedLine_RoutesJobToVendorThenReadyToShip()
    {
        var (order, line, _) = await SeedOrderAsync();
        var vendorId = await SeedVendorAsync();
        var lineItem = OutsourcedItem.CreateForLine(Guid.NewGuid(), order.Id, line.Id,
            new OutsourceDetails(vendorId, "VQ-1", new DateOnly(2026, 9, 1), "ship direct to us"), 120m, TestUserId, DateTime.UtcNow);
        var charge = SalesOrderCharge.Create(Guid.NewGuid(), order.Id, 1, "500 promo pens", null, 500, 1.10m, TestUserId, DateTime.UtcNow);
        var chargeItem = OutsourcedItem.CreateForCharge(Guid.NewGuid(), order.Id, charge.Id,
            new OutsourceDetails(vendorId, "PP-9", null, null), 300m, TestUserId, DateTime.UtcNow);
        _db.SalesOrderCharges.Add(charge);
        _db.OutsourcedItems.AddRange(lineItem, chargeItem);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        // Release: the outsourced line becomes a vendor-routed job with only the receiving steps.
        var created = await _jobs.ScheduleFromSalesOrderAsync(order.Id);
        _db.ChangeTracker.Clear();
        var job = await _db.Jobs.Include(j => j.Operations).SingleAsync(j => j.Id == created.Single().Id);
        job.Should().Satisfy<Job>(j =>
        {
            j.IsOutsourced.Should().BeTrue();
            j.Status.Should().Be(JobStatus.Outsourced);
            j.Operations.OrderBy(o => o.Sequence).Select(o => o.OperationType).Should().Equal(JobOperationType.Inspection, JobOperationType.Pack, JobOperationType.Ship);
        });

        // Both items (line + charge) show on the production list and in the stage badge.
        var counts = await _jobs.GetStatusCountsAsync([JobStatus.Outsourced, JobStatus.PrePress]);
        counts[JobStatus.Outsourced].Should().BeGreaterThanOrEqualTo(2);
        var listed = await _outsourcing.ListAsync(order.OrderNumber, null, "open", false, null, 1, 50);
        listed.Items.Should().HaveCount(2);
        listed.Items.Single(r => r.IsLine).Should().Satisfy<OutsourcedItemRow>(r =>
        {
            r.JobNumber.Should().Be(job.JobNumber);
            r.VendorName.Should().StartWith("Vendor ");
            r.Status.Should().Be(OutsourceItemStatus.Pending);
            r.QuantityRemaining.Should().Be(1000);
        });
        listed.Items.Single(r => !r.IsLine).Description.Should().Be("500 promo pens");

        // Partial receipt keeps the job at the vendor; the balance sends it to ready-to-ship.
        await _outsourcing.MarkSentAsync(lineItem.Id, new DateOnly(2026, 8, 17));
        await _outsourcing.ReceiveAsync(lineItem.Id, 600, new DateOnly(2026, 8, 25), "first box", markComplete: false);
        _db.ChangeTracker.Clear();
        (await _db.Jobs.SingleAsync(j => j.Id == job.Id)).Status.Should().Be(JobStatus.Outsourced);
        (await _outsourcing.GetActionPanelAsync(lineItem.Id))!.Item.Status.Should().Be(OutsourceItemStatus.PartiallyReceived);

        await _outsourcing.ReceiveAsync(lineItem.Id, 400, new DateOnly(2026, 8, 28), null, markComplete: false);
        _db.ChangeTracker.Clear();
        var received = await _db.Jobs.Include(j => j.Operations).SingleAsync(j => j.Id == job.Id);
        received.Status.Should().Be(JobStatus.Rewound, "a fully received outsourced line is ready to ship");
        received.Operations.Single(o => o.OperationType == JobOperationType.Inspection).Status.Should().Be(JobOperationStatus.Complete);
        received.Operations.Where(o => o.OperationType != JobOperationType.Inspection).Should().OnlyContain(o => o.Status == JobOperationStatus.Pending);

        // The charge closes out on its own — no job involved — and drops off the open list.
        await _outsourcing.ReceiveAsync(chargeItem.Id, 480, new DateOnly(2026, 8, 28), "20 short, vendor credited", markComplete: true);
        var open = await _outsourcing.ListAsync(order.OrderNumber, null, "open", false, null, 1, 50);
        open.Items.Should().BeEmpty();
        var all = await _outsourcing.ListAsync(order.OrderNumber, null, "received", false, null, 1, 50);
        all.Items.Should().HaveCount(2).And.OnlyContain(r => r.Status == OutsourceItemStatus.Received);
    }

    [Fact]
    public async Task Release_WhenVendorAlreadyDeliveredBeforeRelease_JobStartsReadyToShip()
    {
        var (order, line, _) = await SeedOrderAsync();
        var item = OutsourcedItem.CreateForLine(Guid.NewGuid(), order.Id, line.Id, new OutsourceDetails(null, null, null, null), null, TestUserId, DateTime.UtcNow);
        _db.OutsourcedItems.Add(item);
        await _db.SaveChangesAsync();
        await _outsourcing.ReceiveAsync(item.Id, 1000, new DateOnly(2026, 8, 20), null, markComplete: false);
        _db.ChangeTracker.Clear();

        var created = await _jobs.ScheduleFromSalesOrderAsync(order.Id);

        _db.ChangeTracker.Clear();
        (await _db.Jobs.SingleAsync(j => j.Id == created.Single().Id)).Status.Should().Be(JobStatus.Rewound);
    }

    [Fact]
    public async Task RemoveAndReaddLineWithSameId_KeepsOutsourcedItem()
    {
        var (order, line, spec) = await SeedOrderAsync();
        var item = OutsourcedItem.CreateForLine(Guid.NewGuid(), order.Id, line.Id,
            new OutsourceDetails(null, "Q-1", null, "note"), 120m, TestUserId, DateTime.UtcNow);
        _db.OutsourcedItems.Add(item);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var tracked = await _db.SalesOrders.Include(o => o.Lines).SingleAsync(o => o.Id == order.Id);
        var oldLine = tracked.Lines.Single();
        _db.SalesOrderLines.RemoveRange(tracked.Lines);
        var replacement = SalesOrderLine.Create(oldLine.Id, order.Id, 1, oldLine.ProductId, "renamed", null,
            oldLine.Quantity, oldLine.UnitPrice, null, spec, TestUserId, DateTime.UtcNow);
        tracked.ReplaceLines([replacement]);
        _db.SalesOrderLines.Add(replacement);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var survived = await _db.OutsourcedItems.AsNoTracking().AnyAsync(o => o.Id == item.Id);
        survived.Should().BeTrue("re-adding a line with the same id must not cascade-delete its outsourced item");
    }

    private async Task<Guid> SeedVendorAsync()
    {
        var now = DateTime.UtcNow;
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var vendor = Supplier.Create(Guid.NewGuid(), $"Vendor {suffix}", $"VN{suffix}"[..10], "Net 30", 5, null, TestUserId, now);
        vendor.SetOutsourceVendor(true, "promo + wide format", TestUserId, now);
        _db.Suppliers.Add(vendor);
        await _db.SaveChangesAsync();
        return vendor.Id;
    }

    private sealed class StubCurrentUserService(Guid userId) : ICurrentUserService
    {
        public Guid? UserId => userId;
        public bool CanEditMasterData => true;
        public Task<Infrastructure.Identity.ApplicationUser?> GetUserAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<Infrastructure.Identity.ApplicationUser?>(null);
    }

    private async Task<(SalesOrder Order, SalesOrderLine Line, LabelSpec Spec)> SeedOrderAsync()
    {
        var now = DateTime.UtcNow;
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var customerId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var stockId = Guid.NewGuid();

        _db.Suppliers.Add(Supplier.Create(supplierId, $"Out Sup {suffix}", $"OS{suffix}"[..10], "Net 30", 7, null, TestUserId, now));
        _db.Customers.Add(Customer.Create(customerId, $"Out Customer {suffix}", $"OC{suffix}"[..10],
            PaymentTerms.Net30, false, 0.45m, CustomerStatus.Active, null, null, TestUserId, now));
        _db.Stocks.Add(Stock.Create(stockId, $"OT{suffix}"[..10], "BOPP", "BOPP", "Acrylic", "PET", 2.3m, 13.5m,
            supplierId, null, 0.85m, 1000m, TestUserId, now));

        var product = Product.Create(
            Guid.NewGuid(), customerId, [customerId], $"OSKU-{suffix}", null, "Outsourced labels", null,
            4, 3, 0.125m, stockId, InkSet.CMYK, "[]", null, null, null, TestUserId, now);
        _db.Products.Add(product);
        foreach (var assignment in product.CustomerAssignments)
        {
            _db.ProductCustomers.Add(assignment);
        }

        var spec = LabelSpec.Create(4m, 3m, 0.125m, 0.0625m, 0.0625m, 0.0625m, stockId, null,
            InkSet.CMYK, 0, 1m, "[]", "[]", 250m, 0.04m, null, null, null);
        var order = SalesOrder.CreateOpen(
            Guid.NewGuid(), $"SO-{suffix}", customerId, null, null, null, now, null, null, null,
            null, 0m, ShippingAddress.Empty, TestUserId, now);
        var line = SalesOrderLine.Create(Guid.NewGuid(), order.Id, 1, product.Id, "Outsourced labels", null,
            1000, 0.25m, null, spec, TestUserId, now);
        order.AddLine(line);
        _db.SalesOrders.Add(order);
        _db.SalesOrderLines.Add(line);
        await _db.SaveChangesAsync();
        return (order, line, spec);
    }
}
