using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LabelsMis.Infrastructure.Tests.Persistence;

public class TransactionFlowPersistenceTests : IAsyncLifetime
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

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private static (Estimate estimate, EstimateLine line) CreateDraftWithLine(
        Guid estimateId,
        string estimateNumber,
        Guid customerId,
        Guid stockId,
        DateTime now,
        Guid userId,
        DateOnly? validUntil = null)
    {
        var estimate = Estimate.CreateDraft(
            estimateId, estimateNumber, customerId, null, null, validUntil, userId, now);
        var line = EstimateLine.Create(
            Guid.NewGuid(), estimate.Id, 1, null, "Flow labels",
            4, 3, 0.125m, 0.0625m, 0.0625m, 0.0625m,
            stockId, InkSet.CMYK, false, "[]", 30, 0.03m, null, null, null, null, userId, now);
        estimate.AddLine(line);
        return (estimate, line);
    }

    [Fact]
    public async Task EstimateToProductToOrder_FlowPersists()
    {
        var now = DateTime.UtcNow;
        var customerId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var estimateId = Guid.NewGuid();

        _db.Suppliers.Add(Supplier.Create(supplierId, "Sup", "SUP", "Net 30", 7, null, TestUserId, now));
        _db.Customers.Add(Customer.Create(customerId, "Flow Customer", "FLOW", "Net 30", false, 0.45m,
            CustomerStatus.Active, null, TestUserId, now));
        _db.Stocks.Add(Stock.Create(stockId, "STK1", "BOPP", "BOPP", "Acrylic", "PET", 2.3m, 13.5m,
            supplierId, null, 0.85m, 1000m, TestUserId, now));
        await _db.SaveChangesAsync();

        var (estimate, line) = CreateDraftWithLine(estimateId, "EST-2026-99999", customerId, stockId, now, TestUserId,
            DateOnly.FromDateTime(now.AddDays(30)));
        line.AddQuantityBreak(EstimateQuantityBreak.Create(
            Guid.NewGuid(), line.Id, 5000, 0.05m, 250m, 180m, 0.28m, "[]", TestUserId, now));
        estimate.MarkSent("/tmp/flow.pdf", TestUserId, now);
        estimate.MarkWon(TestUserId, now);
        _db.Estimates.Add(estimate);
        await _db.SaveChangesAsync();

        var product = Product.Create(
            Guid.NewGuid(), customerId, [customerId], "FLOW-0001", null, "Flow labels", line.Id,
            4, 3, 0.125m, stockId, InkSet.CMYK, "[]", null, null, TestUserId, now);
        _db.Products.Add(product);
        foreach (var assignment in product.CustomerAssignments)
        {
            _db.ProductCustomers.Add(assignment);
        }
        await _db.SaveChangesAsync();

        var order = SalesOrder.CreateOpen(
            Guid.NewGuid(), "SO-2026-99999", customerId, estimateId, "PO-123", now, null, null, TestUserId, now);
        var orderLine = SalesOrderLine.Create(
            Guid.NewGuid(), order.Id, 1, product.Id, line.Id, 5000, 0.05m, null, TestUserId, now);
        order.AddLine(orderLine);
        _db.SalesOrders.Add(order);
        _db.SalesOrderLines.Add(orderLine);
        await _db.SaveChangesAsync();

        var loaded = await _db.SalesOrders
            .Include(o => o.Lines).ThenInclude(l => l.Product)
            .SingleAsync(o => o.Id == order.Id);

        loaded.Lines.Should().HaveCount(1);
        loaded.Lines.First().Product.SourceEstimateLineId.Should().Be(line.Id);
        loaded.Lines.First().UnitPrice.Should().Be(0.05m);
        loaded.SourceEstimateId.Should().Be(estimateId);
    }

    [Fact]
    public async Task Estimate_SentToWon_PersistsStatus()
    {
        var now = DateTime.UtcNow;
        var customerId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var estimateId = Guid.NewGuid();

        _db.Suppliers.Add(Supplier.Create(supplierId, "Sup2", "SUP2", "Net 30", 7, null, TestUserId, now));
        _db.Customers.Add(Customer.Create(customerId, "Won Customer", "WON", "Net 30", false, 0.45m,
            CustomerStatus.Active, null, TestUserId, now));
        _db.Stocks.Add(Stock.Create(stockId, "STK2", "BOPP", "BOPP", "Acrylic", "PET", 2.3m, 13.5m,
            supplierId, null, 0.85m, 1000m, TestUserId, now));

        var (estimate, _) = CreateDraftWithLine(estimateId, "EST-2026-88888", customerId, stockId, now, TestUserId);
        estimate.MarkSent("/tmp/won.pdf", TestUserId, now);
        estimate.MarkWon(TestUserId, now);
        _db.Estimates.Add(estimate);
        await _db.SaveChangesAsync();

        var loaded = await _db.Estimates.SingleAsync(e => e.Id == estimateId);
        loaded.Status.Should().Be(EstimateStatus.Won);
        loaded.WonAt.Should().NotBeNull();
        loaded.PdfFilePath.Should().Be("/tmp/won.pdf");
    }

    [Fact]
    public async Task Estimate_Revision_PreservesSnapshot()
    {
        var now = DateTime.UtcNow;
        var customerId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var estimateId = Guid.NewGuid();

        _db.Suppliers.Add(Supplier.Create(supplierId, "Sup3", $"SUP3-{estimateId:N}"[..12], "Net 30", 7, null, TestUserId, now));
        _db.Customers.Add(Customer.Create(customerId, "Rev Customer", $"REV-{estimateId:N}"[..12], "Net 30", false, 0.45m,
            CustomerStatus.Active, null, TestUserId, now));
        _db.Stocks.Add(Stock.Create(stockId, $"STK-{estimateId:N}"[..12], "BOPP", "BOPP", "Acrylic", "PET", 2.3m, 13.5m,
            supplierId, null, 0.85m, 1000m, TestUserId, now));

        var (estimate, _) = CreateDraftWithLine(estimateId,
            $"EST-{estimateId.ToString("N")[..8].ToUpperInvariant()}", customerId, stockId, now, TestUserId);
        estimate.MarkSent("/tmp/rev.pdf", TestUserId, now);
        estimate.BeginRevision(Guid.NewGuid(), """{"revision":1}""", TestUserId, now);
        _db.Estimates.Add(estimate);
        await _db.SaveChangesAsync();

        var revisions = await _db.EstimateRevisions.Where(r => r.EstimateId == estimateId).ToListAsync();
        revisions.Should().HaveCount(1);
        revisions[0].SnapshotJson.Should().Contain("revision");
        revisions[0].RevisionNumber.Should().Be(1);

        var loaded = await _db.Estimates.SingleAsync(e => e.Id == estimateId);
        loaded.RevisionNumber.Should().Be(2);
        loaded.Status.Should().Be(EstimateStatus.Draft);
    }

    [Fact]
    public async Task Estimate_SecondRevision_LoadsExistingRevisionsBeforeSnapshot()
    {
        var now = DateTime.UtcNow;
        var customerId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var estimateId = Guid.NewGuid();

        _db.Suppliers.Add(Supplier.Create(supplierId, "Sup4", $"SUP4-{estimateId:N}"[..12], "Net 30", 7, null, TestUserId, now));
        _db.Customers.Add(Customer.Create(customerId, "Rev2 Customer", $"REV2-{estimateId:N}"[..12], "Net 30", false, 0.45m,
            CustomerStatus.Active, null, TestUserId, now));
        _db.Stocks.Add(Stock.Create(stockId, $"STK-{estimateId:N}"[..12], "BOPP", "BOPP", "Acrylic", "PET", 2.3m, 13.5m,
            supplierId, null, 0.85m, 1000m, TestUserId, now));

        var (estimate, _) = CreateDraftWithLine(estimateId,
            $"EST-{estimateId.ToString("N")[..8].ToUpperInvariant()}", customerId, stockId, now, TestUserId);
        estimate.MarkSent("/tmp/rev2.pdf", TestUserId, now);
        estimate.BeginRevision(Guid.NewGuid(), """{"revision":1}""", TestUserId, now);
        _db.Estimates.Add(estimate);
        await _db.SaveChangesAsync();

        var reloaded = await _db.Estimates
            .Include(e => e.Lines)
            .Include(e => e.Revisions)
            .SingleAsync(e => e.Id == estimateId);
        reloaded.MarkSent("/tmp/rev2-v2.pdf", TestUserId, now);
        var revision = reloaded.CreateRevisionSnapshot(Guid.NewGuid(), """{"revision":2}""", TestUserId, now);
        _db.EstimateRevisions.Add(revision);

        await _db.SaveChangesAsync();

        var revisions = await _db.EstimateRevisions
            .Where(r => r.EstimateId == estimateId)
            .OrderBy(r => r.RevisionNumber)
            .ToListAsync();
        revisions.Should().HaveCount(2);
        revisions.Select(r => r.RevisionNumber).Should().Equal(1, 2);

        var loaded = await _db.Estimates.SingleAsync(e => e.Id == estimateId);
        loaded.RevisionNumber.Should().Be(3);
        loaded.Status.Should().Be(EstimateStatus.Draft);
    }
}
