using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.ValueObjects;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.Estimates;
using LabelsMis.Web.Services.Products;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Tests;

/// <summary>
/// The product's Die field is the one source of its die: it is stamped onto the product's die-cut
/// finishing rows on save (a finishing task never carries a die of its own), and a product created
/// when an estimate converts takes the die its line was quoted with. Requires the PostgreSQL test
/// database (same convention as <see cref="OutsourcingIntegrationTests"/>).
/// </summary>
public class ProductDieIntegrationTests : IAsyncLifetime
{
    private static readonly Guid TestUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private LabelsMisDbContext _db = null!;
    private ProductService _products = null!;

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

        _products = new ProductService(_db, new StubCurrentUserService(TestUserId));
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task SaveProduct_StampsTheProductDieOntoDieCutRowsOnly()
    {
        var seed = await SeedMasterDataAsync();
        var strayDie = await SeedDieAsync(seed.CustomerId, "stray");

        // The form posts a die on the die-cut row (as legacy products did, carrying the task's die);
        // the product's own Die field wins and the laminate row never gets one.
        var product = await _products.CreateAsync(Form(seed, seed.DieId,
        [
            new FinishingOperationSelectionInput(seed.LaminateOpId, null, null, 0, StockId: seed.StockId),
            new FinishingOperationSelectionInput(seed.DieCutOpId, null, null, 1, DieId: strayDie)
        ]));
        _db.ChangeTracker.Clear();

        var stored = await _db.Products.AsNoTracking().SingleAsync(p => p.Id == product.Id);
        stored.DieId.Should().Be(seed.DieId);
        var rows = EstimateCalculationMapper.DeserializeFinishingOperations(stored.FinishingOperationsJson).OrderBy(r => r.SortOrder).ToList();
        rows.Should().HaveCount(2);
        rows[0].OperationId.Should().Be(seed.LaminateOpId);
        rows[0].DieId.Should().BeNull();
        rows[0].StockId.Should().Be(seed.StockId, "other row fields are kept as posted");
        rows[1].OperationId.Should().Be(seed.DieCutOpId);
        rows[1].DieId.Should().Be(seed.DieId);
        EstimateCalculationMapper.ResolveDieId(stored.FinishingOperationsJson).Should().Be(seed.DieId);

        // Clearing the product's die clears the die-cut row too.
        await _products.UpdateAsync(product.Id, Form(seed, null,
        [
            new FinishingOperationSelectionInput(seed.DieCutOpId, null, null, 0, DieId: strayDie)
        ]));
        _db.ChangeTracker.Clear();

        var cleared = await _db.Products.AsNoTracking().SingleAsync(p => p.Id == product.Id);
        cleared.DieId.Should().BeNull();
        EstimateCalculationMapper.DeserializeFinishingOperations(cleared.FinishingOperationsJson)
            .Single().DieId.Should().BeNull();
    }

    [Fact]
    public async Task EnsureProductForLine_NewProductTakesTheDieTheLineWasQuotedWith()
    {
        var seed = await SeedMasterDataAsync();
        var lineId = await SeedWonEstimateLineAsync(seed, EstimateCalculationMapper.SerializeFinishingOperations(
        [
            new FinishingOperationSelectionInput(seed.DieCutOpId, null, null, 0, DieId: seed.DieId)
        ]));

        var product = await _products.CreateFromEstimateLineAsync(lineId);
        _db.ChangeTracker.Clear();

        var stored = await _db.Products.AsNoTracking().SingleAsync(p => p.Id == product.Id);
        stored.SourceEstimateLineId.Should().Be(lineId);
        stored.DieId.Should().Be(seed.DieId);
    }

    [Fact]
    public async Task EnsureProductForLine_LineWithoutADie_LeavesTheProductDieEmpty()
    {
        var seed = await SeedMasterDataAsync();
        var lineId = await SeedWonEstimateLineAsync(seed, EstimateCalculationMapper.SerializeFinishingOperations(
        [
            new FinishingOperationSelectionInput(seed.DieCutOpId, null, null, 0)
        ]));

        var product = await _products.CreateFromEstimateLineAsync(lineId);

        product.DieId.Should().BeNull();
    }

    private static ProductFormInput Form(MasterData seed, Guid? dieId, IReadOnlyList<FinishingOperationSelectionInput> finishing) => new(
        seed.CustomerId, [seed.CustomerId], null, "Die-stamp test labels", null,
        3m, 2m, 0.125m, seed.StockId, InkSet.CMYK, finishing, dieId, null, null, null);

    private sealed record MasterData(Guid CustomerId, Guid StockId, Guid DieId, Guid DieCutOpId, Guid LaminateOpId, string Suffix);

    private async Task<MasterData> SeedMasterDataAsync()
    {
        var now = DateTime.UtcNow;
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var customerId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var stockId = Guid.NewGuid();

        _db.Suppliers.Add(Supplier.Create(supplierId, $"Die Sup {suffix}", $"DS{suffix}"[..10], "Net 30", 7, null, TestUserId, now));
        _db.Customers.Add(Customer.Create(customerId, $"Die Customer {suffix}", $"DC{suffix}"[..10],
            PaymentTerms.Net30, false, 0.45m, CustomerStatus.Active, null, null, TestUserId, now));
        _db.Stocks.Add(Stock.Create(stockId, $"DT{suffix}"[..10], "BOPP", "BOPP", "Acrylic", "PET", 2.3m, 13.5m,
            supplierId, null, 0.85m, 1000m, TestUserId, now));

        var dieCut = FinishingOperation.Create(Guid.NewGuid(), $"DIE-{suffix}", "Rotary die-cut", FinishingOperationType.DieCut,
            30m, 250m, "Die cutter", 110m, TestUserId, now);
        var laminate = FinishingOperation.Create(Guid.NewGuid(), $"LAM-{suffix}", "Gloss laminate", FinishingOperationType.Laminate,
            15m, 200m, "Laminator", 90m, TestUserId, now);
        _db.FinishingOperations.AddRange(dieCut, laminate);
        await _db.SaveChangesAsync();

        var dieId = await SeedDieAsync(customerId, "product");
        return new MasterData(customerId, stockId, dieId, dieCut.Id, laminate.Id, suffix);
    }

    private async Task<Guid> SeedDieAsync(Guid customerId, string label)
    {
        var die = Die.Create(Guid.NewGuid(), $"Test die ({label}) {Guid.NewGuid():N}", customerId, DieType.Flexible, "Rectangle",
            3m, 2m, 0.125m, 0.125m, 0.125m, 4, 6, 13m, null, null, null, TestUserId, DateTime.UtcNow);
        _db.Dies.Add(die);
        await _db.SaveChangesAsync();
        return die.Id;
    }

    private async Task<Guid> SeedWonEstimateLineAsync(MasterData seed, string finishingJson)
    {
        var now = DateTime.UtcNow;
        var estimate = Estimate.CreateDraft(Guid.NewGuid(), $"EST-T-{seed.Suffix}", seed.CustomerId, null, null, null,
            null, null, 0m, ShippingAddress.Empty, TestUserId, now);
        var line = EstimateLine.Create(Guid.NewGuid(), estimate.Id, 1, null, "Quoted labels",
            3m, 2m, 0.125m, 0.125m, 0.125m, 0.0625m, seed.StockId, InkSet.CMYK, 0, 1m, "[]", finishingJson,
            250m, 0.04m, null, null, null, null, null, null, TestUserId, now);
        estimate.AddLine(line);
        estimate.MarkWon(TestUserId, now);
        _db.Estimates.Add(estimate);
        _db.EstimateLines.Add(line);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return line.Id;
    }

    private sealed class StubCurrentUserService(Guid userId) : ICurrentUserService
    {
        public Guid? UserId => userId;
        public bool CanEditMasterData => true;
        public Task<Infrastructure.Identity.ApplicationUser?> GetUserAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<Infrastructure.Identity.ApplicationUser?>(null);
    }
}
