using FluentAssertions;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Tests;

/// <summary>
/// The number at the end of a document number keeps counting across a new year. Requires the
/// PostgreSQL test database (same convention as <see cref="InvoiceSyncIntegrationTests"/>). The
/// tests number documents in the years 2090 and later and remove those counters afterwards, so the
/// counters other tests number real documents from are left alone.
/// </summary>
public class DocumentNumberServiceIntegrationTests : IAsyncLifetime
{
    private const int FirstTestYear = 2090;
    private LabelsMisDbContext _db = null!;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5432;Database=labels_mis_test;Username=labels_mis;Password=test_password";

        try
        {
            var options = new DbContextOptionsBuilder<LabelsMisDbContext>()
                .UseNpgsql(connectionString)
                .Options;

            _db = new LabelsMisDbContext(options);
            await _db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Integration tests require PostgreSQL. Start docker compose or set ConnectionStrings__Default.", ex);
        }

        await RemoveTestYearsAsync();
    }

    public async Task DisposeAsync()
    {
        await RemoveTestYearsAsync();
        await _db.DisposeAsync();
    }

    public static TheoryData<string> DocumentTypes => new()
    {
        DocumentNumberService.EstimateType,
        DocumentNumberService.SalesOrderType,
        DocumentNumberService.JobType,
        DocumentNumberService.PurchaseOrderType,
        DocumentNumberService.RollType,
        DocumentNumberService.ShipmentType,
        DocumentNumberService.InvoiceType
    };

    [Theory]
    [MemberData(nameof(DocumentTypes))]
    public async Task NextNumber_FirstOfANewYear_CarriesOnFromTheLastNumber(string documentType)
    {
        var clock = new YearClock(FirstTestYear);
        var numbers = new DocumentNumberService(_db, clock);
        await NextAsync(numbers, documentType);
        var lastOfTheYear = await NextAsync(numbers, documentType);

        clock.Year = FirstTestYear + 1;
        var firstOfTheNext = await NextAsync(numbers, documentType);
        var second = await NextAsync(numbers, documentType);

        YearOf(lastOfTheYear).Should().Be(FirstTestYear);
        YearOf(firstOfTheNext).Should().Be(FirstTestYear + 1, "the year in the number still changes");
        NumberOf(firstOfTheNext).Should().Be(NumberOf(lastOfTheYear) + 1, "the number does not start again");
        NumberOf(second).Should().Be(NumberOf(lastOfTheYear) + 2);
    }

    [Fact]
    public async Task NextInvoiceNumber_AfterAYearWithNoInvoices_StillCarriesOn()
    {
        var clock = new YearClock(FirstTestYear);
        var numbers = new DocumentNumberService(_db, clock);
        var last = await numbers.NextInvoiceNumberAsync();

        clock.Year = FirstTestYear + 3;
        var next = await numbers.NextInvoiceNumberAsync();

        next.Should().StartWith($"INV-{FirstTestYear + 3}-");
        NumberOf(next).Should().Be(NumberOf(last) + 1);
    }

    [Fact]
    public async Task NextInvoiceNumber_OfANewYear_FollowsTheNumbersAlreadyGivenOut()
    {
        // Whatever the counters hold when the change goes live (production's 2026 rows), the first
        // number of the next year is one more than the highest of them.
        var reached = await _db.DocumentSequences.AsNoTracking()
            .Where(s => s.DocumentType == DocumentNumberService.InvoiceType)
            .MaxAsync(s => (int?)s.LastNumber) ?? 0;

        var first = await new DocumentNumberService(_db, new YearClock(FirstTestYear)).NextInvoiceNumberAsync();

        first.Should().Be($"INV-{FirstTestYear}-{reached + 1:D5}");
    }

    [Fact]
    public async Task NextInvoiceNumber_OfOneType_LeavesTheOtherTypesNumbersAlone()
    {
        var clock = new YearClock(FirstTestYear);
        var numbers = new DocumentNumberService(_db, clock);
        var job = await numbers.NextJobNumberAsync();
        for (var i = 0; i < 3; i++)
        {
            await numbers.NextInvoiceNumberAsync();
        }

        clock.Year = FirstTestYear + 1;
        var nextJob = await numbers.NextJobNumberAsync();

        NumberOf(nextJob).Should().Be(NumberOf(job) + 1, "each document type counts by itself");
    }

    private static Task<string> NextAsync(DocumentNumberService numbers, string documentType) => documentType switch
    {
        DocumentNumberService.EstimateType => numbers.NextEstimateNumberAsync(),
        DocumentNumberService.SalesOrderType => numbers.NextSalesOrderNumberAsync(),
        DocumentNumberService.JobType => numbers.NextJobNumberAsync(),
        DocumentNumberService.PurchaseOrderType => numbers.NextPurchaseOrderNumberAsync(),
        DocumentNumberService.RollType => numbers.NextRollBarcodeAsync(),
        DocumentNumberService.ShipmentType => numbers.NextShipmentNumberAsync(),
        DocumentNumberService.InvoiceType => numbers.NextInvoiceNumberAsync(),
        _ => throw new ArgumentOutOfRangeException(nameof(documentType))
    };

    private static int YearOf(string number) => int.Parse(number.Split('-')[^2]);

    private static int NumberOf(string number) => int.Parse(number.Split('-')[^1]);

    private Task RemoveTestYearsAsync() =>
        _db.DocumentSequences.Where(s => s.Year >= FirstTestYear).ExecuteDeleteAsync();

    private sealed class YearClock(int year) : TimeProvider
    {
        public int Year { get; set; } = year;

        public override DateTimeOffset GetUtcNow() => new(Year, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
