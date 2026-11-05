using FluentAssertions;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Estimating;
using LabelsMis.Domain.Invoicing;
using LabelsMis.Domain.Shipping;
using LabelsMis.Domain.ValueObjects;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.Estimates;
using LabelsMis.Web.Services.Invoices;
using LabelsMis.Web.Services.Jobs;
using LabelsMis.Web.Services.Pdfs;
using LabelsMis.Web.Services.Rolls;
using LabelsMis.Web.Services.Settings;
using LabelsMis.Web.Services.Shipments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LabelsMis.Web.Tests;

/// <summary>
/// Production's Mark shipped must leave a recorded shipment behind so the order counts as shipped
/// and its invoice can bill: hand-off methods get a package-less shipment automatically (one per
/// order for jobs marked shipped together), methods that need the shipment record refuse, and a job
/// marked shipped before the rule can be put right. Requires the PostgreSQL test database (same
/// convention as <see cref="InvoiceSyncIntegrationTests"/>).
/// </summary>
public class JobShippingIntegrationTests : IAsyncLifetime
{
    private static readonly Guid TestUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private LabelsMisDbContext _db = null!;
    private InvoiceService _invoices = null!;
    private ShipmentService _shipments = null!;
    private JobService _jobs = null!;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5432;Database=labels_mis_test;Username=labels_mis;Password=test_password";
        _db = new LabelsMisDbContext(new DbContextOptionsBuilder<LabelsMisDbContext>().UseNpgsql(connectionString).Options);
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
        var settings = new StubGeneralSettingsService(null);
        _invoices = new InvoiceService(
            _db,
            user,
            numbers,
            settings,
            Options.Create(new InvoiceOptions()),
            new LabelsMis.Web.Services.Email.DocumentEmailService(_db, user, new NoopEmailSender()),
            new LabelsMis.Web.Pdf.InvoicePdfGenerator(settings, new TempPdfStorage(null!)));
        _shipments = new ShipmentService(_db, user, numbers, null!, _invoices);
        _jobs = new JobService(_db, user, numbers, new EstimateCalculationMapper(_db), new EstimatingService(),
            new RollService(_db, user, numbers), _shipments);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task MarkShipped_PickupOrder_RecordsAPackagelessShipment_AndTheInvoiceIsReady()
    {
        var (order, jobs) = await SeedOrderAsync(ShippingMethodType.Pickup, 500);
        var invoice = await _invoices.CreateFromSalesOrderAsync(order.Id);

        var shipment = await _jobs.AdvanceJobStatusAsync(jobs[0].Id, JobStatus.Shipped);

        shipment.Should().NotBeNull();
        _db.ChangeTracker.Clear();
        var saved = await _db.Shipments.AsNoTracking().Include(s => s.Lines).Include(s => s.Packages)
            .SingleAsync(s => s.SalesOrderId == order.Id);
        saved.Status.Should().Be(ShipmentStatus.InTransit);
        saved.Packages.Should().BeEmpty();
        saved.BilledShipping.Should().Be(0m, "pickup bills no shipping");
        saved.Lines.Should().ContainSingle().Which.QuantityShipped.Should().Be(500);
        (await _db.Jobs.AsNoTracking().SingleAsync(j => j.Id == jobs[0].Id)).Status.Should().Be(JobStatus.Shipped);
        (await _db.SalesOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).Status.Should().Be(SalesOrderStatus.Shipped);
        (await _invoices.GetWaitingReasonsAsync(invoice.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task MarkShipped_DeliveryOrderJobByJob_GoesOutAsOneShipmentWithOneDeliveryFee()
    {
        var (order, jobs) = await SeedOrderAsync(ShippingMethodType.Delivery, 300, 200);
        var invoice = await _invoices.CreateFromSalesOrderAsync(order.Id);
        var fee = (await _db.GeneralSettings.AsNoTracking().Select(g => (decimal?)g.LocalDeliveryFee).FirstOrDefaultAsync())
            ?? ShippingBillingSettings.Default.LocalDeliveryFee;

        var first = await _jobs.AdvanceJobStatusAsync(jobs[0].Id, JobStatus.Shipped);
        (await _db.SalesOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).Status
            .Should().NotBe(SalesOrderStatus.Shipped, "line 2 hasn't shipped yet");
        var second = await _jobs.AdvanceJobStatusAsync(jobs[1].Id, JobStatus.Shipped);

        second!.Id.Should().Be(first!.Id, "jobs on one order marked shipped together share the shipment");
        _db.ChangeTracker.Clear();
        var shipments = await _db.Shipments.AsNoTracking().Include(s => s.Lines)
            .Where(s => s.SalesOrderId == order.Id).ToListAsync();
        shipments.Should().ContainSingle();
        shipments[0].Lines.Select(l => l.QuantityShipped).Should().BeEquivalentTo([300, 200]);
        shipments[0].BilledShipping.Should().Be(fee);
        (await _db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoice.Id)).ShippingAmount.Should().Be(fee);
        (await _db.SalesOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).Status.Should().Be(SalesOrderStatus.Shipped);
        (await _invoices.GetWaitingReasonsAsync(invoice.Id)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(ShippingMethodType.Fedex)]
    [InlineData(null)]
    public async Task MarkShipped_MethodNeedingTheShipmentRecord_IsRefusedAndNothingMoves(ShippingMethodType? type)
    {
        var (order, jobs) = await SeedOrderAsync(type, 250);

        var advance = () => _jobs.AdvanceJobStatusAsync(jobs[0].Id, JobStatus.Shipped);
        var setClosed = () => _jobs.SetJobStatusAsync(jobs[0].Id, JobStatus.Closed);
        var oneClick = () => _shipments.MarkShippedAsync(order.Id);

        (await advance.Should().ThrowAsync<ShipmentRecordRequiredException>())
            .Which.SalesOrderId.Should().Be(order.Id);
        await setClosed.Should().ThrowAsync<ShipmentRecordRequiredException>();
        await oneClick.Should().ThrowAsync<ShipmentRecordRequiredException>();
        _db.ChangeTracker.Clear();
        (await _db.Jobs.AsNoTracking().SingleAsync(j => j.Id == jobs[0].Id)).Status.Should().Be(JobStatus.Rewound);
        (await _db.Shipments.AsNoTracking().AnyAsync(s => s.SalesOrderId == order.Id)).Should().BeFalse();
        (await _shipments.GetOrderForManualShipmentAsync(order.Id))!.Lines.Single().IsReady
            .Should().BeTrue("Record shipment still takes the Rewound job");
    }

    [Fact]
    public async Task SetStatus_ClosingAPickupJob_RecordsItsShipmentToo()
    {
        var (order, jobs) = await SeedOrderAsync(ShippingMethodType.Pickup, 100);

        (await _jobs.SetJobStatusAsync(jobs[0].Id, JobStatus.Closed)).Should().NotBeNull();

        _db.ChangeTracker.Clear();
        (await _db.Jobs.AsNoTracking().SingleAsync(j => j.Id == jobs[0].Id)).Status.Should().Be(JobStatus.Closed);
        (await _db.SalesOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).Status.Should().Be(SalesOrderStatus.Shipped);
    }

    [Fact]
    public async Task JobMarkedShippedBeforeTheRule_IsNamedOnTheInvoice_AndCanBePutRight()
    {
        var (order, jobs) = await SeedOrderAsync(ShippingMethodType.Pickup, 400);
        var invoice = await _invoices.CreateFromSalesOrderAsync(order.Id);
        var tracked = await _db.Jobs.SingleAsync(j => j.Id == jobs[0].Id);
        tracked.SetStatus(JobStatus.Shipped, TestUserId, DateTime.UtcNow);
        await _db.SaveChangesAsync();

        (await _invoices.GetWaitingReasonsAsync(invoice.Id)).Should().Equal(
            $"Not shipped yet (0 of 1 lines). {jobs[0].JobNumber} was marked shipped with no shipment recorded — "
            + InvoiceReadinessRules.UnrecordedShipmentHint);
        (await _shipments.GetJobShippingStateAsync(jobs[0].Id))!.IsMarkedShippedWithoutShipment(JobStatus.Shipped)
            .Should().BeTrue();
        (await _shipments.GetOrderForManualShipmentAsync(order.Id))!.Lines.Single().IsReady
            .Should().BeTrue("Record shipment can take a job production already marked shipped");

        var shipment = await _shipments.RecordJobShippedAsync(jobs[0].Id);

        shipment.Should().NotBeNull();
        _db.ChangeTracker.Clear();
        (await _db.Jobs.AsNoTracking().SingleAsync(j => j.Id == jobs[0].Id)).Status.Should().Be(JobStatus.Shipped);
        (await _db.SalesOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).Status.Should().Be(SalesOrderStatus.Shipped);
        (await _invoices.GetWaitingReasonsAsync(invoice.Id)).Should().BeEmpty();
        (await _shipments.RecordJobShippedAsync(jobs[0].Id)).Should().BeNull("the line has shipped in full");
    }

    [Fact]
    public async Task UnderRunLine_IsNotOfferedForASecondShipment()
    {
        var (order, jobs) = await SeedOrderAsync(ShippingMethodType.Pickup, 1000);
        await _jobs.AdvanceJobStatusAsync(jobs[0].Id, JobStatus.Shipped);
        // The shipment actually went out 50 short (edit the recorded quantity, as an under-run would leave it).
        var line = await _db.ShipmentLines.SingleAsync(l => l.Shipment.SalesOrderId == order.Id);
        _db.Entry(line).Property(l => l.QuantityShipped).CurrentValue = 950;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        (await _shipments.GetOrderForManualShipmentAsync(order.Id))!.Lines.Single().IsReady.Should().BeFalse();
        (await _shipments.GetJobShippingStateAsync(jobs[0].Id))!.IsMarkedShippedWithoutShipment(JobStatus.Shipped)
            .Should().BeFalse("it shipped on a recorded shipment; Mark order shipped finishes an under-run");
    }

    /// <summary>A set-up customer (tax form back, billing contact) with an order of one line per
    /// quantity on a method of the given type (null = no method), each line with a Rewound job.</summary>
    private async Task<(SalesOrder Order, List<Job> Jobs)> SeedOrderAsync(ShippingMethodType? methodType, params int[] quantities)
    {
        var now = DateTime.UtcNow;
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var customerId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var stockId = Guid.NewGuid();

        Guid? methodId = null;
        if (methodType is { } type)
        {
            methodId = Guid.NewGuid();
            _db.ShippingMethods.Add(ShippingMethod.Create(methodId.Value, $"Ship {type} {suffix}", type,
                type == ShippingMethodType.Fedex ? 35m : 0m, type != ShippingMethodType.Pickup, TestUserId, now));
        }

        _db.Suppliers.Add(Supplier.Create(supplierId, $"Ship Sup {suffix}", $"SS{suffix}"[..10], "Net 30", 7, null, TestUserId, now));
        var customer = Customer.Create(customerId, $"Ship Customer {suffix}", $"SC{suffix}"[..10],
            PaymentTerms.Net30, false, 0.45m, CustomerStatus.Active, null, null, TestUserId, now);
        customer.SetQuickBooksSetup($"QB-{suffix}", true, null, TestUserId, now);
        _db.Customers.Add(customer);
        _db.Contacts.Add(Contact.Create(Guid.NewGuid(), customerId, "Bea", "Biller", $"ap-{suffix}@example.com",
            null, "Billing", true, TestUserId, now));
        _db.Stocks.Add(Stock.Create(stockId, $"ST{suffix}"[..10], "BOPP", "BOPP", "Acrylic", "PET", 2.3m, 13.5m,
            supplierId, null, 0.85m, 1000m, TestUserId, now));
        var product = Product.Create(
            Guid.NewGuid(), customerId, [customerId], $"SKU-{suffix}", null, "Ship test labels", null,
            4, 3, 0.125m, stockId, InkSet.CMYK, "[]", null, null, null, TestUserId, now);
        _db.Products.Add(product);
        foreach (var assignment in product.CustomerAssignments)
        {
            _db.ProductCustomers.Add(assignment);
        }

        var order = SalesOrder.CreateOpen(
            Guid.NewGuid(), $"SO-{suffix}", customerId, null, null, null, now, null, null, null,
            methodId, 0m, ShippingAddress.Empty, TestUserId, now);
        _db.SalesOrders.Add(order);
        var jobs = new List<Job>();
        for (var i = 0; i < quantities.Length; i++)
        {
            var line = SalesOrderLine.Create(
                Guid.NewGuid(), order.Id, i + 1, product.Id, $"Ship test labels {i + 1}", null, quantities[i], 0.5m, null,
                null, TestUserId, now);
            order.AddLine(line);
            _db.SalesOrderLines.Add(line);
            var job = Job.CreatePlanned(Guid.NewGuid(), $"JOB-T-{suffix}-{i + 1}", line.Id, product.Id,
                quantities[i], quantities[i], null, 0, null, null, TestUserId, now);
            job.SetStatus(JobStatus.Rewound, TestUserId, now);
            _db.Jobs.Add(job);
            jobs.Add(job);
        }

        await _db.SaveChangesAsync();
        return (order, jobs);
    }

    private sealed class StubCurrentUserService(Guid userId) : ICurrentUserService
    {
        public Guid? UserId => userId;
        public bool CanEditMasterData => true;
        public Task<Infrastructure.Identity.ApplicationUser?> GetUserAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<Infrastructure.Identity.ApplicationUser?>(null);
    }

    private sealed class NoopEmailSender : LabelsMis.Domain.Email.IEmailSender
    {
        public Task SendAsync(
            string to,
            string subject,
            string body,
            IReadOnlyList<string>? attachmentPaths = null,
            string? cc = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubGeneralSettingsService(GeneralSettings? settings) : GeneralSettingsService(null!, null!)
    {
        public override Task<GeneralSettings?> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(settings);
    }
}
