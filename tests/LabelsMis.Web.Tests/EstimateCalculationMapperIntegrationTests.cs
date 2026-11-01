using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Estimating;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.Estimates;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Tests;

/// <summary>
/// The click rate the estimate mapper resolves from Setup → Inks. A line whose ink set has no active
/// ink record is refused (an error, no prices) instead of costing its clicks at $0. Requires the
/// PostgreSQL test database (same convention as <see cref="InvoiceSyncIntegrationTests"/>); both
/// tests rewrite the EPM/EPMW ink rows of that throwaway database, so they live in one class.
/// </summary>
public class EstimateCalculationMapperIntegrationTests : IAsyncLifetime
{
    private static readonly Guid TestUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private LabelsMisDbContext _db = null!;
    private EstimateCalculationMapper _mapper = null!;

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

        if (!await _db.Presses.AnyAsync(p => p.Id == Press.Indigo6800Id))
        {
            _db.Presses.Add(Press.CreateIndigo6800(TestUserId, DateTime.UtcNow));
            await _db.SaveChangesAsync();
        }

        _mapper = new EstimateCalculationMapper(_db);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task BuildRequest_InkSetWithoutActiveInkRecord_IsRefusedInsteadOfPricedAtZero()
    {
        var (customerId, stockId) = await SeedCustomerAndStockAsync();
        await RetireProcessInksAsync(InkSet.EPM, InkSet.EPMW);

        var request = await _mapper.BuildRequestAsync(customerId, Line(stockId, InkSet.EPM));
        var result = new EstimatingService().Calculate(request);

        request.ClickRateMissing.Should().BeTrue();
        result.QuantityBreaks.Should().BeEmpty();
        result.Errors.Should().ContainSingle()
            .Which.Should().Be("No ink record for EPM — clicks can't be costed. Add one in Setup → Inks.");
    }

    [Fact]
    public async Task BuildRequest_EpmInkRecord_PricesClicksAtItsRateAndEpmwFallsBackToIt()
    {
        var (customerId, stockId) = await SeedCustomerAndStockAsync();
        await RetireProcessInksAsync(InkSet.EPM, InkSet.EPMW);
        _db.Inks.Add(Ink.Create(Guid.NewGuid(), $"EPM-{Guid.NewGuid():N}"[..20], "Test EPM", InkSet.EPM, 11.50m,
            false, null, 0m, 0m, 0m, 1m, TestUserId, DateTime.UtcNow));
        await _db.SaveChangesAsync();

        var epm = await _mapper.BuildRequestAsync(customerId, Line(stockId, InkSet.EPM));
        var epmw = await _mapper.BuildRequestAsync(customerId, Line(stockId, InkSet.EPMW));

        epm.ClickRateMissing.Should().BeFalse();
        epm.ClickRatePer1000.Should().Be(11.50m);
        epmw.ClickRateMissing.Should().BeFalse();
        epmw.ClickRatePer1000.Should().Be(11.50m, "EPMW's process separations are the EPM three");
        new EstimatingService().Calculate(epm).Errors.Should().BeEmpty();

        var press = await _db.Presses.AsNoTracking().SingleAsync(p => p.Id == Press.Indigo6800Id);
        (epm.PressSpeedFpm3Separations, epm.PressSpeedFpm4Separations, epm.PressSpeedFpm5PlusSeparations)
            .Should().Be((press.SpeedFpm3Separations, press.SpeedFpm4Separations, press.SpeedFpm5PlusSeparations),
                "the press's separation speeds reach the engine");
    }

    private async Task RetireProcessInksAsync(params InkSet[] inkSets)
    {
        var inks = await _db.Inks.Where(i => i.IsActive && !i.IsSpot && inkSets.Contains(i.InkSet)).ToListAsync();
        foreach (var ink in inks)
        {
            ink.Deactivate(TestUserId, DateTime.UtcNow);
        }

        await _db.SaveChangesAsync();
    }

    private async Task<(Guid CustomerId, Guid StockId)> SeedCustomerAndStockAsync()
    {
        var now = DateTime.UtcNow;
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var customerId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        _db.Suppliers.Add(Supplier.Create(supplierId, $"Ink Sup {suffix}", $"IS{suffix}"[..10], "Net 30", 7, null, TestUserId, now));
        _db.Customers.Add(Customer.Create(customerId, $"Ink Customer {suffix}", $"IC{suffix}"[..10],
            PaymentTerms.Net30, false, 0.45m, CustomerStatus.Active, null, null, TestUserId, now));
        _db.Stocks.Add(Stock.Create(stockId, $"IT{suffix}"[..10], "B7771 white BOPP", "BOPP", "Acrylic", "PET", 2.3m, 13.0m,
            supplierId, null, 0.455m, 1000m, TestUserId, now));
        await _db.SaveChangesAsync();
        return (customerId, stockId);
    }

    private static EstimateLineFormInput Line(Guid stockId, InkSet inkSet) => new(
        null, null, "16 oz can label", 4.938m, 8m, 0.125m, 0.63m, 0m, 0m, stockId, inkSet,
        0, 1m, [], [], 30m, 0.03m, null, [10000], 1.0m, 2, null);
}
