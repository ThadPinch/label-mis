using LabelsMis.Domain.Entities;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Infrastructure.Tests.Persistence;

/// <summary>
/// Regression tests for editing a Stock across request boundaries (load in one context,
/// mutate, save). Recording a cost change adds a StockCostHistory child to an already-tracked
/// Stock; because the Guid key is app-assigned, EF change detection used to mistake the new
/// row for an existing one and emit an UPDATE (0 rows affected -> concurrency exception).
/// The child must be inserted. See StockService.UpdateAsync.
/// </summary>
public class StockUpdateRepro : IAsyncLifetime
{
    private LabelsMisDbContext _db = null!;
    private static readonly Guid TestUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Default")
        ?? "Host=localhost;Port=5432;Database=labels_mis_test;Username=labels_mis;Password=test_password";

    private static LabelsMisDbContext NewContext() =>
        new(new DbContextOptionsBuilder<LabelsMisDbContext>().UseNpgsql(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        _db = NewContext();
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
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task<Guid> SeedStockAsync(string code)
    {
        var supplierId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        _db.Suppliers.Add(Supplier.Create(supplierId, "Sup " + code, code, "Net 30", 3, null, TestUserId, now));
        var stockId = Guid.NewGuid();
        var stock = Stock.Create(stockId, code, "Desc", "BOPP", "Perm", "Liner",
            2.0m, 13.5m, supplierId, "PN", 0.85m, 1000m, TestUserId, now);
        stock.RecordCostChange(Guid.NewGuid(), 0.85m, now.Date, TestUserId, now);
        _db.Stocks.Add(stock);
        await _db.SaveChangesAsync();
        return stockId;
    }

    [Fact]
    public async Task Edit_NonCostValue_Persists()
    {
        var stockId = await SeedStockAsync("REPRO-" + Guid.NewGuid().ToString("N")[..6]);

        await using var db2 = NewContext();
        var now = DateTime.UtcNow;
        var loaded = await db2.Stocks.Include(s => s.CostHistory).FirstAsync(s => s.Id == stockId);
        loaded.Update(loaded.Code, "Desc EDITED", loaded.FaceMaterial, loaded.Adhesive, loaded.Liner,
            loaded.TotalCaliperMil, 14.0m, loaded.SupplierId, loaded.SupplierPartNumber,
            loaded.CostPerMsi, loaded.MinOrderQtyLf, TestUserId, now);
        await db2.SaveChangesAsync();

        await using var db3 = NewContext();
        var reloaded = await db3.Stocks.FirstAsync(s => s.Id == stockId);
        reloaded.Description.Should().Be("Desc EDITED");
        reloaded.WidthIn.Should().Be(14.0m);
    }

    [Fact]
    public async Task Edit_CostValue_InsertsHistoryAndPersists()
    {
        var stockId = await SeedStockAsync("REPRO-" + Guid.NewGuid().ToString("N")[..6]);

        await using var db2 = NewContext();
        var now = DateTime.UtcNow;
        var loaded = await db2.Stocks.Include(s => s.CostHistory).FirstAsync(s => s.Id == stockId);

        var history = loaded.RecordCostChange(Guid.NewGuid(), 0.99m, now.Date, TestUserId, now);
        db2.StockCostHistory.Add(history); // mirrors StockService.UpdateAsync fix
        loaded.Update(loaded.Code, loaded.Description, loaded.FaceMaterial, loaded.Adhesive, loaded.Liner,
            loaded.TotalCaliperMil, loaded.WidthIn, loaded.SupplierId, loaded.SupplierPartNumber,
            0.99m, loaded.MinOrderQtyLf, TestUserId, now);
        await db2.SaveChangesAsync();

        await using var db3 = NewContext();
        var reloaded = await db3.Stocks.Include(s => s.CostHistory).FirstAsync(s => s.Id == stockId);
        reloaded.CostPerMsi.Should().Be(0.99m);
        reloaded.CostHistory.Should().HaveCount(2);
    }
}
