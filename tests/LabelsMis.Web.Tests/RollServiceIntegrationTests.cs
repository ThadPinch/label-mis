using FluentAssertions;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.Rolls;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Tests;

/// <summary>
/// Integration tests for the roll-label data and the consume-material picker list on
/// <see cref="RollService"/>. Requires the PostgreSQL test database (same convention as
/// <see cref="InvoiceSyncIntegrationTests"/>).
/// </summary>
public class RollServiceIntegrationTests : IAsyncLifetime
{
    private static readonly Guid TestUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private LabelsMisDbContext _db = null!;
    private RollService _rolls = null!;

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

        _rolls = new RollService(_db, new StubCurrentUserService(TestUserId), new DocumentNumberService(_db));
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task GetLabelAsync_ReturnsStockSupplierAndRollFacts()
    {
        var (stockId, stockCode) = await SeedStockAsync();
        var rollId = await _rolls.AddManualAsync(new ManualRollInput(stockId, "LOT-77", 6.5m, 5000m, "Rack A3"));

        var label = await _rolls.GetLabelAsync(rollId);

        label.Should().NotBeNull();
        label!.StockCode.Should().Be(stockCode);
        label.SupplierName.Should().StartWith("Roll Sup");
        label.LotNumber.Should().Be("LOT-77");
        label.WidthIn.Should().Be(6.5m);
        label.OriginalLengthLf.Should().Be(5000m);
        label.RemainingLengthLf.Should().Be(5000m);
        label.Location.Should().Be("Rack A3");
        label.PoNumber.Should().BeNull("a manually added roll has no receipt");
        label.Status.Should().Be(nameof(RollStatus.Available));
    }

    [Fact]
    public async Task GetLabelAsync_UnknownRoll_ReturnsNull()
    {
        var label = await _rolls.GetLabelAsync(Guid.NewGuid());

        label.Should().BeNull();
    }

    [Fact]
    public async Task ListPickerOptionsAsync_ListsRollsWithMaterialAndSkipsScrappedAndDepleted()
    {
        var (stockId, stockCode) = await SeedStockAsync();
        var available = await _rolls.AddManualAsync(new ManualRollInput(stockId, "LOT-A", 13m, 1000m, "Rack B1"));
        var staged = await _rolls.AddManualAsync(new ManualRollInput(stockId, "LOT-B", 13m, 800m, null));
        var scrapped = await _rolls.AddManualAsync(new ManualRollInput(stockId, "LOT-C", 13m, 500m, null));
        var depleted = await _rolls.AddManualAsync(new ManualRollInput(stockId, "LOT-D", 13m, 200m, null));
        await _rolls.StageAsync(staged, "Press side");
        await _rolls.ScrapAsync(scrapped, "damaged");
        await _rolls.ConsumeAsync(depleted, 200m, null, null);

        var options = await _rolls.ListPickerOptionsAsync();

        var ours = options.Where(o => o.StockId == stockId).ToList();
        ours.Select(o => o.Id).Should().BeEquivalentTo([available, staged]);
        ours.Should().AllSatisfy(o =>
        {
            o.StockCode.Should().Be(stockCode);
            o.RemainingLengthLf.Should().BePositive();
        });
        ours.Single(o => o.Id == staged).Should().Satisfy<RollPickerOption>(o =>
        {
            o.Status.Should().Be(RollStatus.Staged);
            o.Location.Should().Be("Press side");
        });
    }

    private async Task<(Guid StockId, string Code)> SeedStockAsync()
    {
        var now = DateTime.UtcNow;
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var supplierId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var code = $"RL{suffix}"[..10];

        _db.Suppliers.Add(Supplier.Create(supplierId, $"Roll Sup {suffix}", $"RS{suffix}"[..10], "Net 30", 7, null, TestUserId, now));
        _db.Stocks.Add(Stock.Create(stockId, code, "White matte BOPP", "BOPP", "Acrylic", "PET", 2.6m, 13m,
            supplierId, "GRI-1", 0.85m, 1000m, TestUserId, now));
        await _db.SaveChangesAsync();
        return (stockId, code);
    }

    private sealed class StubCurrentUserService(Guid userId) : ICurrentUserService
    {
        public Guid? UserId => userId;
        public bool CanEditMasterData => true;
        public Task<Infrastructure.Identity.ApplicationUser?> GetUserAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<Infrastructure.Identity.ApplicationUser?>(null);
    }
}
