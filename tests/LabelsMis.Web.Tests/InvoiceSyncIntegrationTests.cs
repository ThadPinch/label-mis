using FluentAssertions;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.ValueObjects;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services;
using LabelsMis.Web.Services.Invoices;
using LabelsMis.Web.Services.Pdfs;
using LabelsMis.Web.Services.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LabelsMis.Web.Tests;

/// <summary>
/// Integration tests for keeping invoices in step with sales order price edits:
/// draft invoices are rebuilt by <see cref="InvoiceService.SyncDraftFromSalesOrderAsync"/>,
/// sent invoices are skipped, and voiding unblocks generating a replacement.
/// Requires the PostgreSQL test database (same convention as LabelsMis.Infrastructure.Tests).
/// </summary>
public class InvoiceSyncIntegrationTests : IAsyncLifetime
{
    private static readonly Guid TestUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private LabelsMisDbContext _db = null!;
    private InvoiceService _invoiceService = null!;

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

        // Null settings fall back to InvoiceOptions.DefaultTaxRate, keeping the expected
        // tax math independent of whatever GeneralSettings row the test database carries.
        var settings = new StubGeneralSettingsService(null);
        _invoiceService = new InvoiceService(
            _db,
            new StubCurrentUserService(TestUserId),
            new DocumentNumberService(_db),
            settings,
            Options.Create(new InvoiceOptions()),
            new NoopEmailSender(),
            new LabelsMis.Web.Pdf.InvoicePdfGenerator(settings, new TempPdfStorage(null!)));
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task PriceEdit_RebuildsDraftInvoiceLinesAndTotals()
    {
        var order = await SeedOrderAsync(quantity: 1000, unitPrice: 0.50m, chargeAmount: 75m);
        var invoice = await _invoiceService.CreateFromSalesOrderAsync(order.Id);

        invoice.Subtotal.Should().Be(575m);
        invoice.Lines.Should().HaveCount(2);

        var line = await _db.SalesOrderLines.SingleAsync(l => l.SalesOrderId == order.Id);
        line.Update(1000, 0.60m, null, TestUserId, DateTime.UtcNow);
        await _db.SaveChangesAsync();

        var result = await _invoiceService.SyncDraftFromSalesOrderAsync(order.Id);

        result.Outcome.Should().Be(InvoiceSyncOutcome.Updated);
        result.InvoiceNumber.Should().Be(invoice.InvoiceNumber);

        var lines = await _db.InvoiceLines.AsNoTracking()
            .Where(l => l.InvoiceId == invoice.Id)
            .OrderBy(l => l.LineNumber)
            .ToListAsync();
        lines.Should().HaveCount(2, "the old lines are replaced, not appended to");
        lines[0].UnitPrice.Should().Be(0.60m);
        lines[0].LineTotal.Should().Be(600m);
        lines[1].LineTotal.Should().Be(75m);

        var reloaded = await _db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoice.Id);
        reloaded.Subtotal.Should().Be(675m);
        reloaded.TaxAmount.Should().Be(Math.Round(675m * 0.0825m, 4, MidpointRounding.AwayFromZero));
        reloaded.Total.Should().Be(reloaded.Subtotal + reloaded.TaxAmount + reloaded.ShippingAmount);
        reloaded.BalanceDue.Should().Be(reloaded.Total);
        reloaded.Status.Should().Be(InvoiceStatus.Draft);
    }

    [Fact]
    public async Task SentInvoice_IsSkipped_AndVoidingAllowsReplacementAtCurrentPrices()
    {
        var order = await SeedOrderAsync(quantity: 500, unitPrice: 0.40m, chargeAmount: null);
        var original = await _invoiceService.CreateFromSalesOrderAsync(order.Id);

        var tracked = await _db.Invoices.SingleAsync(i => i.Id == original.Id);
        tracked.MarkSent(null, TestUserId, DateTime.UtcNow);
        await _db.SaveChangesAsync();

        var line = await _db.SalesOrderLines.SingleAsync(l => l.SalesOrderId == order.Id);
        line.Update(500, 0.55m, null, TestUserId, DateTime.UtcNow);
        await _db.SaveChangesAsync();

        var result = await _invoiceService.SyncDraftFromSalesOrderAsync(order.Id);

        result.Outcome.Should().Be(InvoiceSyncOutcome.SkippedSent);
        (await _db.InvoiceLines.AsNoTracking().SingleAsync(l => l.InvoiceId == original.Id))
            .UnitPrice.Should().Be(0.40m, "a sent invoice must not be rewritten");

        await _invoiceService.VoidAsync(original.Id, "Price correction", default);

        var replacement = await _invoiceService.CreateFromSalesOrderAsync(order.Id);

        replacement.Id.Should().NotBe(original.Id);
        replacement.InvoiceNumber.Should().NotBe(original.InvoiceNumber);
        replacement.Status.Should().Be(InvoiceStatus.Draft);
        replacement.Subtotal.Should().Be(275m, "the replacement bills the order's current prices");

        // While the replacement is live, generating again must return it, not another invoice.
        (await _invoiceService.CreateFromSalesOrderAsync(order.Id)).Id.Should().Be(replacement.Id);
    }

    private async Task<SalesOrder> SeedOrderAsync(int quantity, decimal unitPrice, decimal? chargeAmount)
    {
        var now = DateTime.UtcNow;
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var customerId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var stockId = Guid.NewGuid();

        _db.Suppliers.Add(Supplier.Create(supplierId, $"Sync Sup {suffix}", $"SS{suffix}"[..10], "Net 30", 7, null, TestUserId, now));
        _db.Customers.Add(Customer.Create(customerId, $"Sync Customer {suffix}", $"SC{suffix}"[..10],
            PaymentTerms.Net30, false, 0.45m, CustomerStatus.Active, null, TestUserId, now));
        _db.Stocks.Add(Stock.Create(stockId, $"ST{suffix}"[..10], "BOPP", "BOPP", "Acrylic", "PET", 2.3m, 13.5m,
            supplierId, null, 0.85m, 1000m, TestUserId, now));

        var product = Product.Create(
            Guid.NewGuid(), customerId, [customerId], $"SKU-{suffix}", null, "Sync test labels", null,
            4, 3, 0.125m, stockId, InkSet.CMYK, "[]", null, null, TestUserId, now);
        _db.Products.Add(product);
        foreach (var assignment in product.CustomerAssignments)
        {
            _db.ProductCustomers.Add(assignment);
        }

        var order = SalesOrder.CreateOpen(
            Guid.NewGuid(), $"SO-{suffix}", customerId, null, null, null, now, null, null, null,
            null, 0m, ShippingAddress.Empty, TestUserId, now);
        var orderLine = SalesOrderLine.Create(
            Guid.NewGuid(), order.Id, 1, product.Id, "Sync test labels", null, quantity, unitPrice, null,
            null, TestUserId, now);
        order.AddLine(orderLine);
        _db.SalesOrders.Add(order);
        _db.SalesOrderLines.Add(orderLine);

        if (chargeAmount is { } amount)
        {
            var charge = SalesOrderCharge.Create(
                Guid.NewGuid(), order.Id, 1, "Die creation", null, 1, amount, TestUserId, now);
            order.ReplaceCharges([charge]);
            _db.SalesOrderCharges.Add(charge);
        }

        await _db.SaveChangesAsync();
        return order;
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
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>Bypasses the DbContext so the tax rate falls back to <see cref="InvoiceOptions.DefaultTaxRate"/>.</summary>
    private sealed class StubGeneralSettingsService(GeneralSettings? settings) : GeneralSettingsService(null!, null!)
    {
        public override Task<GeneralSettings?> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(settings);
    }
}
