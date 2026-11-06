using FluentAssertions;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Search;
using LabelsMis.Domain.ValueObjects;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.Jobs;
using LabelsMis.Web.Services.Search;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Tests;

/// <summary>
/// The jobs floor board and the global search bar against a real database: each job's floor
/// location comes from its route (current finishing step, who is clocked on), and search finds a
/// job by its number, a customer by a contact's email, and only in the categories it is given.
/// Requires the PostgreSQL test database (same convention as <see cref="JobShippingIntegrationTests"/>).
/// </summary>
public class JobFloorAndSearchIntegrationTests : IAsyncLifetime
{
    private static readonly Guid TestUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private LabelsMisDbContext _db = null!;
    private JobFloorService _floor = null!;
    private GlobalSearchService _search = null!;

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

        var clock = new ClientClock(new HttpContextAccessor(), TimeProvider.System);
        _floor = new JobFloorService(_db, clock);
        _search = new GlobalSearchService(_db, _floor, new PathOnlyLinks());
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task ListAsync_FinishingJob_IsOnItsFirstUnfinishedStep()
    {
        var seed = await SeedAsync();

        var jobs = await _floor.GetAsync([seed.FinishingJob.Id]);

        jobs.Should().ContainSingle().Which.Location.Should().Satisfy<Domain.Jobs.JobFloorLocation>(l =>
        {
            l.StageLabel.Should().Be("Finishing");
            l.Where.Should().Be(seed.DieCutLabel);
            l.Detail.Should().Be("Step 2 of 2");
            l.Route.Select(s => s.State).Should().Equal(
                Domain.Jobs.FloorStepState.Done,       // pre-press
                Domain.Jobs.FloorStepState.Done,       // press
                Domain.Jobs.FloorStepState.Done,       // laminate
                Domain.Jobs.FloorStepState.Current,    // die cut
                Domain.Jobs.FloorStepState.Upcoming,   // QC
                Domain.Jobs.FloorStepState.Upcoming,   // pack
                Domain.Jobs.FloorStepState.Upcoming);  // ship
        });
    }

    [Fact]
    public async Task ListAsync_PressJobWithSomeoneClockedOn_IsRunningAndNamesThem()
    {
        var seed = await SeedAsync();

        var jobs = await _floor.GetAsync([seed.PressJob.Id]);

        jobs.Should().ContainSingle().Which.Location.Should().Satisfy<Domain.Jobs.JobFloorLocation>(l =>
        {
            l.StageLabel.Should().Be("Press");
            l.Where.Should().StartWith("Printing on");
            l.IsRunning.Should().BeTrue();
            l.RunningBy.Should().Be("test");
        });
    }

    [Fact]
    public async Task ListAsync_StatusAndCustomerFilters_ReturnOnlyThatCustomersStage()
    {
        var seed = await SeedAsync();

        var result = await _floor.ListAsync(
            new FloorQuery(Statuses: [JobStatus.Printed], CustomerId: seed.CustomerId), 1, 100);
        var counts = await _floor.CountByStatusAsync(new FloorQuery(CustomerId: seed.CustomerId));

        result.Items.Select(j => j.Id).Should().Equal(seed.FinishingJob.Id);
        counts.Should().Contain(JobStatus.Printed, 1).And.Contain(JobStatus.Queued, 1);
    }

    [Fact]
    public async Task SearchAsync_FullJobNumber_JumpsToThatJob()
    {
        var seed = await SeedAsync();

        var results = await _search.SearchAsync(seed.FinishingJob.JobNumber, AllCategories, 5);

        results.Groups[0].Category.Should().Be(SearchCategory.Job);
        results.JumpTarget.Should().NotBeNull();
        results.JumpTarget!.Id.Should().Be(seed.FinishingJob.Id);
        results.JumpTarget.Badge.Should().Be($"Finishing · {seed.DieCutLabel}");
    }

    [Fact]
    public async Task SearchAsync_ContactEmail_FindsTheCustomer()
    {
        var seed = await SeedAsync();

        var results = await _search.SearchAsync(seed.ContactEmail, AllCategories, 5);

        results.Groups.Should().Contain(g => g.Category == SearchCategory.Customer)
            .Which.Hits.Should().ContainSingle(h => h.Id == seed.CustomerId)
            .Which.Subtitle.Should().Contain(seed.ContactEmail);
    }

    [Fact]
    public async Task SearchAsync_OnlyQueriesTheCategoriesItIsGiven()
    {
        var seed = await SeedAsync();

        var results = await _search.SearchAsync(seed.Suffix, new HashSet<SearchCategory> { SearchCategory.SalesOrder }, 5);

        results.Groups.Should().ContainSingle().Which.Category.Should().Be(SearchCategory.SalesOrder);
    }

    private static readonly IReadOnlySet<SearchCategory> AllCategories = Enum.GetValues<SearchCategory>().ToHashSet();

    private sealed record Seed(
        string Suffix,
        Guid CustomerId,
        string ContactEmail,
        Job FinishingJob,
        Job PressJob,
        string DieCutLabel);

    /// <summary>A customer (with a contact), one product with two finishing steps, and an order with
    /// two jobs: one in Finishing with its laminate step done, one on press with someone clocked on.</summary>
    private async Task<Seed> SeedAsync()
    {
        var now = DateTime.UtcNow;
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var customerId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var email = $"floor-{suffix.ToLowerInvariant()}@example.com";

        _db.Suppliers.Add(Supplier.Create(supplierId, $"Floor Sup {suffix}", $"FS{suffix}"[..10], "Net 30", 7, null, TestUserId, now));
        _db.Customers.Add(Customer.Create(customerId, $"Floor Customer {suffix}", $"FC{suffix}"[..10],
            PaymentTerms.Net30, false, 0.45m, CustomerStatus.Active, null, null, TestUserId, now));
        _db.Contacts.Add(Contact.Create(Guid.NewGuid(), customerId, "Fran", "Floor", email, null, null, true, false, TestUserId, now));
        _db.Stocks.Add(Stock.Create(stockId, $"FT{suffix}"[..10], "BOPP", "BOPP", "Acrylic", "PET", 2.3m, 13.5m,
            supplierId, null, 0.85m, 1000m, TestUserId, now));

        var laminate = FinishingOperation.Create(Guid.NewGuid(), $"LAM{suffix}"[..10], $"Laminate {suffix}",
            FinishingOperationType.Laminate, 10, 100, "Laminator", 60, TestUserId, now);
        var dieCut = FinishingOperation.Create(Guid.NewGuid(), $"DIE{suffix}"[..10], $"Die cut {suffix}",
            FinishingOperationType.DieCut, 10, 100, "Die station", 60, TestUserId, now);
        _db.FinishingOperations.AddRange(laminate, dieCut);

        var product = Product.Create(
            Guid.NewGuid(), customerId, [customerId], $"FSKU-{suffix}", null, $"Floor labels {suffix}", null,
            4, 3, 0.125m, stockId, InkSet.CMYK, "[]", null, null, null, TestUserId, now);
        _db.Products.Add(product);
        foreach (var assignment in product.CustomerAssignments)
        {
            _db.ProductCustomers.Add(assignment);
        }

        var order = SalesOrder.CreateOpen(
            Guid.NewGuid(), $"SO-F{suffix}", customerId, null, null, $"PO{suffix}", now, null, null, null,
            null, 0m, ShippingAddress.Empty, TestUserId, now);
        _db.SalesOrders.Add(order);

        Job AddJob(int lineNumber, JobStatus status)
        {
            var line = SalesOrderLine.Create(Guid.NewGuid(), order.Id, lineNumber, product.Id, $"Floor labels {suffix} {lineNumber}",
                null, 1000, 0.5m, null, null, TestUserId, now);
            order.AddLine(line);
            _db.SalesOrderLines.Add(line);
            var job = Job.CreatePlanned(Guid.NewGuid(), $"JOB-F{suffix}-{lineNumber}", line.Id, product.Id,
                1000, 1050, null, 5, null, null, TestUserId, now);
            job.SetStatus(status, TestUserId, now);
            _db.Jobs.Add(job);
            return job;
        }

        JobOperation AddOperation(Job job, int sequence, JobOperationType type, EquipmentType equipment, Guid? equipmentId)
        {
            var operation = JobOperation.Create(Guid.NewGuid(), job.Id, sequence, type, equipment, equipmentId, 30, TestUserId, now);
            job.AddOperation(operation);
            _db.JobOperations.Add(operation);
            return operation;
        }

        var finishingJob = AddJob(1, JobStatus.Printed);
        AddOperation(finishingJob, 1, JobOperationType.Press, EquipmentType.Press, Press.Indigo6800Id).Complete(TestUserId, now);
        AddOperation(finishingJob, 2, JobOperationType.Finishing, EquipmentType.FinishingOperation, laminate.Id).Complete(TestUserId, now);
        AddOperation(finishingJob, 3, JobOperationType.Finishing, EquipmentType.FinishingOperation, dieCut.Id);
        AddOperation(finishingJob, 4, JobOperationType.Inspection, EquipmentType.None, null);
        AddOperation(finishingJob, 5, JobOperationType.Pack, EquipmentType.None, null);
        AddOperation(finishingJob, 6, JobOperationType.Ship, EquipmentType.None, null);

        var pressJob = AddJob(2, JobStatus.Queued);
        var press = AddOperation(pressJob, 1, JobOperationType.Press, EquipmentType.Press, Press.Indigo6800Id);
        press.Start(TestUserId, now, TestUserId, now);
        _db.JobTimeEntries.Add(press.ClockOn(Guid.NewGuid(), TestUserId, now));
        AddOperation(pressJob, 2, JobOperationType.Inspection, EquipmentType.None, null);

        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return new Seed(suffix, customerId, email, finishingJob, pressJob, dieCut.Description);
    }

    /// <summary>Stands in for routing: every page link becomes "/page-name/id".</summary>
    private sealed class PathOnlyLinks : LinkGenerator
    {
        public override string? GetPathByAddress<TAddress>(
            HttpContext httpContext, TAddress address, RouteValueDictionary values, RouteValueDictionary? ambientValues = null,
            PathString? pathBase = null, FragmentString fragment = default, LinkOptions? options = null) =>
            Describe(address);

        public override string? GetPathByAddress<TAddress>(
            TAddress address, RouteValueDictionary values, PathString pathBase = default, FragmentString fragment = default,
            LinkOptions? options = null) =>
            Describe(address);

        public override string? GetUriByAddress<TAddress>(
            HttpContext httpContext, TAddress address, RouteValueDictionary values, RouteValueDictionary? ambientValues = null,
            string? scheme = null, HostString? host = null, PathString? pathBase = null, FragmentString fragment = default,
            LinkOptions? options = null) => null;

        public override string? GetUriByAddress<TAddress>(
            TAddress address, RouteValueDictionary values, string? scheme, HostString host, PathString pathBase = default,
            FragmentString fragment = default, LinkOptions? options = null) => null;

        private static string Describe<TAddress>(TAddress address) =>
            address is RouteValuesAddress route
                ? $"{route.ExplicitValues["page"]}/{route.ExplicitValues["id"]}"
                : "/";
    }
}
