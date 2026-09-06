using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LabelsMis.Infrastructure.Tests.Persistence;

public class MasterDataPersistenceTests : IAsyncLifetime
{
    private LabelsMisDbContext _db = null!;
    private static readonly Guid TestUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

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
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task Customer_PersistsAndReadsBack()
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var customer = Customer.Create(id, "Acme Labels", "ACME", PaymentTerms.Net30, false, 0.45m,
            CustomerStatus.Active, null, null, TestUserId, now);
        customer.AddAddress(Address.Create(Guid.NewGuid(), id, AddressType.Billing, "123 Main St", null,
            "Chicago", "IL", "60601", "US", true, TestUserId, now));
        customer.AddContact(Contact.Create(Guid.NewGuid(), id, "Jane", "Doe", "jane@acme.com", null,
            "Buyer", true, TestUserId, now));

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var loaded = await _db.Customers
            .Include(c => c.Addresses)
            .Include(c => c.Contacts)
            .SingleAsync(c => c.Id == id);

        loaded.Name.Should().Be("Acme Labels");
        loaded.Addresses.Should().HaveCount(1);
        loaded.Contacts.Should().HaveCount(1);
    }

    [Fact]
    public async Task Supplier_PersistsAndReadsBack()
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var supplier = Supplier.Create(id, "Fasson", "FASSON", "Net 30", 5, "ACC-1", TestUserId, now);
        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync();

        var loaded = await _db.Suppliers.SingleAsync(s => s.Id == id);
        loaded.Code.Should().Be("FASSON");
    }

    [Fact]
    public async Task Stock_PersistsAndReadsBack()
    {
        var supplierId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        _db.Suppliers.Add(Supplier.Create(supplierId, "Supplier", "SUP1", "Net 30", 3, null, TestUserId, now));

        var stockId = Guid.NewGuid();
        var stock = Stock.Create(stockId, "BOPP-WHT", "White BOPP", "BOPP", "Permanent", "40# SCK",
            2.0m, 13.5m, supplierId, "PN-1", 0.85m, 1000m, TestUserId, now);
        _db.Stocks.Add(stock);
        await _db.SaveChangesAsync();

        var loaded = await _db.Stocks.Include(s => s.Supplier).SingleAsync(s => s.Id == stockId);
        loaded.Supplier.Name.Should().Be("Supplier");
        loaded.CostPerMsi.Should().Be(0.85m);
    }

    [Fact]
    public async Task Die_PersistsAndReadsBack()
    {
        var id = Guid.NewGuid();
        var die = Die.Create(id, "Shop die 4x3", null, DieType.Flexible, "Rectangle",
            4.0m, 3.0m, 0.125m, 0.0625m, 0.0625m, 3, 1, 13.0m, null, null, "Shelf A",
            TestUserId, DateTime.UtcNow);
        _db.Dies.Add(die);
        await _db.SaveChangesAsync();

        var loaded = await _db.Dies.SingleAsync(d => d.Id == id);
        loaded.LabelsAcross.Should().Be(3);
    }

    [Fact]
    public async Task Ink_PersistsAndReadsBack()
    {
        var id = Guid.NewGuid();
        var ink = Ink.Create(id, "CMYK-STD", "CMYK standard", InkSet.CMYK, 35m, false, null, 0m, 1500m, 0m, 1m, TestUserId, DateTime.UtcNow);
        _db.Inks.Add(ink);
        await _db.SaveChangesAsync();

        var loaded = await _db.Inks.SingleAsync(i => i.Id == id);
        loaded.ClickRatePer1000.Should().Be(35m);
    }

    [Fact]
    public async Task FinishingOperation_PersistsAndReadsBack()
    {
        var id = Guid.NewGuid();
        var op = FinishingOperation.Create(
            id,
            "LAM-GLOSS",
            "Gloss laminate",
            FinishingOperationType.Laminate,
            15m,
            200m,
            "Laminator",
            90m,
            TestUserId,
            DateTime.UtcNow);
        _db.FinishingOperations.Add(op);
        await _db.SaveChangesAsync();

        var loaded = await _db.FinishingOperations.SingleAsync(o => o.Id == id);
        loaded.OperationType.Should().Be(FinishingOperationType.Laminate);
    }

    [Fact]
    public async Task ShippingMethod_PersistsAndReadsBack()
    {
        var id = Guid.NewGuid();
        var method = ShippingMethod.Create(
            id,
            "FedEx Ground",
            ShippingMethodType.Fedex,
            35m,
            requiresAddress: true,
            TestUserId,
            DateTime.UtcNow);
        _db.ShippingMethods.Add(method);
        await _db.SaveChangesAsync();

        var loaded = await _db.ShippingMethods.SingleAsync(m => m.Id == id);
        loaded.MethodType.Should().Be(ShippingMethodType.Fedex);
        loaded.Price.Should().Be(35m);
        loaded.RequiresAddress.Should().BeTrue();
        loaded.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Press_PersistsAndReadsBack()
    {
        var id = Guid.NewGuid();
        var press = Press.Create(
            id,
            "Test Press",
            "TEST1",
            PressType.DigitalInkjet,
            webWidthIn: 13.39m,
            minWebWidthIn: 7.87m,
            maxWebWidthIn: 13.39m,
            maxImageWidthIn: 12.59m,
            frameRepeatIn: 18.9m,
            maxImageLengthIn: 38.58m,
            maxRepeatIn: 38.58m,
            minRepeatIn: 18.9m,
            frameFactor: 2,
            maxColors: 7,
            speedFpm: 100m,
            setupMinutes: 20m,
            costPerHour: 150m,
            isClickBased: true,
            TestUserId,
            DateTime.UtcNow);
        _db.Presses.Add(press);
        await _db.SaveChangesAsync();

        var loaded = await _db.Presses.SingleAsync(p => p.Id == id);
        loaded.IsClickBased.Should().BeTrue();
    }
}
