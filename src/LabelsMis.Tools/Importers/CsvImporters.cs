using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.ValueObjects;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LabelsMis.Tools.Importers;

public record ImportResult(int SuccessCount, int SkippedCount, IReadOnlyList<string> Errors);

public abstract class CsvImporterBase
{
    protected static async Task<LabelsMisDbContext> CreateDbContextAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<LabelsMisDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var db = new LabelsMisDbContext(options);
        await db.Database.MigrateAsync();
        return db;
    }

    protected static string ResolveConnectionString(string? overrideConnection)
    {
        if (!string.IsNullOrWhiteSpace(overrideConnection))
        {
            return overrideConnection;
        }

        var config = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "..", "LabelsMis.Web"))
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        return config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not configured.");
    }

    protected static async Task<(List<T> Rows, List<string> Errors)> ReadCsvAsync<T>(string csvPath) where T : class
    {
        var errors = new List<string>();
        await using var stream = File.OpenRead(csvPath);
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        csv.Context.Configuration.HeaderValidated = null;
        csv.Context.Configuration.MissingFieldFound = null;
        var rows = new List<T>();
        await foreach (var record in csv.GetRecordsAsync<T>())
        {
            rows.Add(record);
        }

        return (rows, errors);
    }
}

public sealed class CustomerCsvRow
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Terms { get; set; } = "Net 30";
    public bool TaxExempt { get; set; }
    public decimal DefaultMarkupPct { get; set; } = 0.45m;
}

public sealed class CustomerImporter : CsvImporterBase
{
    public static async Task<ImportResult> ImportAsync(string csvPath, Guid actorId, string? connectionString = null)
    {
        var errors = new List<string>();
        var (rows, readErrors) = await ReadCsvAsync<CustomerCsvRow>(csvPath);
        errors.AddRange(readErrors);
        var success = 0;
        var skipped = 0;

        await using var db = await CreateDbContextAsync(ResolveConnectionString(connectionString));
        var now = DateTime.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            foreach (var (row, index) in rows.Select((r, i) => (r, i + 2)))
            {
                if (string.IsNullOrWhiteSpace(row.Code) || string.IsNullOrWhiteSpace(row.Name))
                {
                    errors.Add($"Row {index}: Code and Name are required.");
                    continue;
                }

                var code = row.Code.Trim().ToUpperInvariant();
                if (await db.Customers.AnyAsync(c => c.Code == code))
                {
                    skipped++;
                    continue;
                }

                var customer = Customer.Create(
                    Guid.NewGuid(), row.Name.Trim(), code, row.Terms.Trim(),
                    row.TaxExempt, row.DefaultMarkupPct, CustomerStatus.Active, null, actorId, now);
                db.Customers.Add(customer);
                success++;
            }

            if (errors.Count > 0)
            {
                await tx.RollbackAsync();
                return new ImportResult(0, skipped, errors);
            }

            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return new ImportResult(success, skipped, errors);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            errors.Add(ex.Message);
            return new ImportResult(0, skipped, errors);
        }
    }
}

public sealed class StockCsvRow
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SupplierCode { get; set; } = string.Empty;
    public decimal WidthIn { get; set; }
    public decimal CostPerMsi { get; set; }
}

public sealed class StockImporter : CsvImporterBase
{
    public static async Task<ImportResult> ImportAsync(string csvPath, Guid actorId, string? connectionString = null)
    {
        var errors = new List<string>();
        var (rows, _) = await ReadCsvAsync<StockCsvRow>(csvPath);
        var success = 0;
        var skipped = 0;

        await using var db = await CreateDbContextAsync(ResolveConnectionString(connectionString));
        var now = DateTime.UtcNow;
        var suppliers = await db.Suppliers.ToDictionaryAsync(s => s.Code);

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            foreach (var (row, index) in rows.Select((r, i) => (r, i + 2)))
            {
                var code = row.Code.Trim().ToUpperInvariant();
                if (await db.Stocks.AnyAsync(s => s.Code == code))
                {
                    skipped++;
                    continue;
                }

                if (!suppliers.TryGetValue(row.SupplierCode.Trim().ToUpperInvariant(), out var supplier))
                {
                    errors.Add($"Row {index}: Supplier '{row.SupplierCode}' not found.");
                    continue;
                }

                var stock = Stock.Create(
                    Guid.NewGuid(), code, row.Description.Trim(), "Face", "Adhesive", "Liner",
                    2.0m, row.WidthIn, supplier.Id, null, row.CostPerMsi, 1000m, actorId, now);
                db.Stocks.Add(stock);
                success++;
            }

            if (errors.Count > 0)
            {
                await tx.RollbackAsync();
                return new ImportResult(0, skipped, errors);
            }

            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return new ImportResult(success, skipped, errors);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            errors.Add(ex.Message);
            return new ImportResult(0, skipped, errors);
        }
    }
}

public sealed class ProductCsvRow
{
    public string CustomerCode { get; set; } = string.Empty;
    public string InternalSku { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal LabelAcrossIn { get; set; }
    public decimal LabelAroundIn { get; set; }
    public string StockCode { get; set; } = string.Empty;
}

public sealed class ProductImporter : CsvImporterBase
{
    public static async Task<ImportResult> ImportAsync(string csvPath, Guid actorId, string? connectionString = null)
    {
        var errors = new List<string>();
        var (rows, _) = await ReadCsvAsync<ProductCsvRow>(csvPath);
        var success = 0;
        var skipped = 0;

        await using var db = await CreateDbContextAsync(ResolveConnectionString(connectionString));
        var now = DateTime.UtcNow;
        var customers = await db.Customers.ToDictionaryAsync(c => c.Code);
        var stocks = await db.Stocks.ToDictionaryAsync(s => s.Code);

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            foreach (var (row, index) in rows.Select((r, i) => (r, i + 2)))
            {
                var sku = row.InternalSku.Trim().ToUpperInvariant();
                if (await db.Products.AnyAsync(p => p.InternalSku == sku))
                {
                    skipped++;
                    continue;
                }

                if (!customers.TryGetValue(row.CustomerCode.Trim().ToUpperInvariant(), out var customer))
                {
                    errors.Add($"Row {index}: Customer '{row.CustomerCode}' not found.");
                    continue;
                }

                if (!stocks.TryGetValue(row.StockCode.Trim().ToUpperInvariant(), out var stock))
                {
                    errors.Add($"Row {index}: Stock '{row.StockCode}' not found.");
                    continue;
                }

                var product = Product.Create(
                    Guid.NewGuid(), customer.Id, [customer.Id], sku, null, row.Description.Trim(), null,
                    row.LabelAcrossIn, row.LabelAroundIn, 0.125m, stock.Id, InkSet.CMYK, "[]",
                    null, null, actorId, now);
                db.Products.Add(product);
                success++;
            }

            if (errors.Count > 0)
            {
                await tx.RollbackAsync();
                return new ImportResult(0, skipped, errors);
            }

            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return new ImportResult(success, skipped, errors);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            errors.Add(ex.Message);
            return new ImportResult(0, skipped, errors);
        }
    }
}

public sealed class OpeningBalanceInvoiceCsvRow
{
    public string CustomerCode { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal BalanceDue { get; set; }
    public DateOnly InvoiceDate { get; set; }
}

public sealed class OpeningBalanceImporter : CsvImporterBase
{
    public static async Task<ImportResult> ImportAsync(string csvPath, Guid actorId, string? connectionString = null)
    {
        var errors = new List<string>();
        var (rows, _) = await ReadCsvAsync<OpeningBalanceInvoiceCsvRow>(csvPath);
        var success = 0;
        var skipped = 0;

        await using var db = await CreateDbContextAsync(ResolveConnectionString(connectionString));
        var now = DateTime.UtcNow;
        var customers = await db.Customers.ToDictionaryAsync(c => c.Code);

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            foreach (var (row, index) in rows.Select((r, i) => (r, i + 2)))
            {
                if (await db.Invoices.AnyAsync(i => i.InvoiceNumber == row.InvoiceNumber))
                {
                    skipped++;
                    continue;
                }

                if (!customers.TryGetValue(row.CustomerCode.Trim().ToUpperInvariant(), out var customer))
                {
                    errors.Add($"Row {index}: Customer '{row.CustomerCode}' not found.");
                    continue;
                }

                var order = SalesOrder.CreateOpen(
                    Guid.NewGuid(), $"OB-{row.InvoiceNumber}", customer.Id, null, "OPENING-BALANCE",
                    now, null, "Opening balance import", null, 0m, ShippingAddress.Empty, actorId, now);
                db.SalesOrders.Add(order);

                var invoice = Invoice.CreateDraft(
                    Guid.NewGuid(), row.InvoiceNumber, customer.Id, order.Id, null,
                    row.InvoiceDate, row.InvoiceDate.AddDays(30), row.BalanceDue, 0, 0,
                    "Opening AR balance", actorId, now);
                invoice.MarkSent(null, actorId, now);
                db.Invoices.Add(invoice);
                success++;
            }

            if (errors.Count > 0)
            {
                await tx.RollbackAsync();
                return new ImportResult(0, skipped, errors);
            }

            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return new ImportResult(success, skipped, errors);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            errors.Add(ex.Message);
            return new ImportResult(0, skipped, errors);
        }
    }
}
