using FluentAssertions;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Infrastructure.Tests.Persistence;

/// <summary>
/// Regression tests for editing a Product's customer assignments across request boundaries
/// (load in one context, mutate, save). Re-saving a product while keeping a customer it already
/// had used to clear and re-create every ProductCustomer row; EF then deleted and re-inserted the
/// same (ProductId, CustomerId), which collides on the unique index and throws
/// DbUpdateConcurrencyException ("expected 1 row, affected 0"). Unchanged assignments must be left
/// in place. See Product.ReplaceCustomerAssignments and ProductService.UpdateAsync.
/// </summary>
public class ProductCustomerUpdateRepro : IAsyncLifetime
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

    private async Task<(Guid ProductId, Guid CustomerAId, Guid CustomerBId)> SeedProductAsync(string suffix)
    {
        var now = DateTime.UtcNow;

        var supplierId = Guid.NewGuid();
        _db.Suppliers.Add(Supplier.Create(supplierId, "Sup " + suffix, "SUP" + suffix, "Net 30", 3, null, TestUserId, now));

        var substrateId = Guid.NewGuid();
        _db.Stocks.Add(Stock.Create(substrateId, "STK" + suffix, "Substrate", "BOPP", "Perm", "Liner",
            2.0m, 13.5m, supplierId, "PN", 0.85m, 1000m, TestUserId, now));

        var customerAId = Guid.NewGuid();
        var customerBId = Guid.NewGuid();
        _db.Customers.Add(Customer.Create(customerAId, "Cust A " + suffix, "CA" + suffix,
            PaymentTerms.Net30, false, 0.35m, CustomerStatus.Active, null, null, TestUserId, now));
        _db.Customers.Add(Customer.Create(customerBId, "Cust B " + suffix, "CB" + suffix,
            PaymentTerms.Net30, false, 0.35m, CustomerStatus.Active, null, null, TestUserId, now));

        var productId = Guid.NewGuid();
        var product = Product.Create(productId, customerAId, [customerAId], "SKU-" + suffix, null,
            "Product " + suffix, null, 2m, 3m, 0m, substrateId, InkSet.CMYK, "[]", null, null, null, TestUserId, now);
        _db.Products.Add(product);

        await _db.SaveChangesAsync();
        return (productId, customerAId, customerBId);
    }

    [Fact]
    public async Task Edit_KeepingSameCustomer_Persists()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var (productId, customerAId, _) = await SeedProductAsync(suffix);

        await using var db2 = NewContext();
        var now = DateTime.UtcNow;
        var loaded = await db2.Products
            .Include(p => p.CustomerAssignments)
            .FirstAsync(p => p.Id == productId);

        loaded.Update(loaded.CustomerSku, "Product EDITED", loaded.LabelAcrossIn, loaded.LabelAroundIn,
            loaded.CornerRadiusIn, loaded.SubstrateId, loaded.InkSet, loaded.FinishingOperationsJson,
            loaded.DieId, loaded.ArtworkFilePath, loaded.Notes, TestUserId, now);
        // Same customer stays assigned — this is the case that used to throw.
        var (added, removed) = loaded.SetCustomers(customerAId, [customerAId], TestUserId, now);
        db2.ProductCustomers.AddRange(added); // mirrors ProductService.UpdateAsync
        db2.ProductCustomers.RemoveRange(removed);
        await db2.SaveChangesAsync();

        await using var db3 = NewContext();
        var reloaded = await db3.Products
            .Include(p => p.CustomerAssignments)
            .FirstAsync(p => p.Id == productId);
        reloaded.Description.Should().Be("Product EDITED");
        reloaded.CustomerAssignments.Should().ContainSingle()
            .Which.CustomerId.Should().Be(customerAId);
    }

    [Fact]
    public async Task Edit_AddingAndRemovingCustomers_Persists()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var (productId, customerAId, customerBId) = await SeedProductAsync(suffix);

        await using var db2 = NewContext();
        var now = DateTime.UtcNow;
        var loaded = await db2.Products
            .Include(p => p.CustomerAssignments)
            .FirstAsync(p => p.Id == productId);

        // Keep A, add B: A is unchanged (must not be deleted+reinserted), B is new.
        var (added, removed) = loaded.SetCustomers(customerAId, [customerAId, customerBId], TestUserId, now);
        db2.ProductCustomers.AddRange(added);
        db2.ProductCustomers.RemoveRange(removed);
        await db2.SaveChangesAsync();

        await using var db3 = NewContext();
        var reloaded = await db3.Products
            .Include(p => p.CustomerAssignments)
            .FirstAsync(p => p.Id == productId);
        reloaded.CustomerAssignments.Select(a => a.CustomerId)
            .Should().BeEquivalentTo([customerAId, customerBId]);

        // Now drop A, keep B.
        await using var db4 = NewContext();
        var loaded2 = await db4.Products
            .Include(p => p.CustomerAssignments)
            .FirstAsync(p => p.Id == productId);
        var (added2, removed2) = loaded2.SetCustomers(customerBId, [customerBId], TestUserId, DateTime.UtcNow);
        db4.ProductCustomers.AddRange(added2);
        db4.ProductCustomers.RemoveRange(removed2);
        await db4.SaveChangesAsync();

        await using var db5 = NewContext();
        var reloaded2 = await db5.Products
            .Include(p => p.CustomerAssignments)
            .FirstAsync(p => p.Id == productId);
        reloaded2.CustomerAssignments.Should().ContainSingle()
            .Which.CustomerId.Should().Be(customerBId);
    }
}
