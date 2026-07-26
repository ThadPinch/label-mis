using System.Globalization;
using System.Text;
using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Reports;

public enum ReportType
{
    Invoices = 0,
    Payments = 1,
    SalesOrders = 2,
    Estimates = 3,
    Jobs = 4,
    Shipments = 5,
    PurchaseOrders = 6
}

public record GeneratedReport(
    string Title,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>Optional narrowing filters; each report applies the ones that make sense for it.</summary>
public record ReportFilters(
    Guid? CustomerId = null,
    Guid? ProductId = null,
    Guid? DieId = null,
    Guid? SupplierId = null);

/// <summary>
/// Ad-hoc finance/operations report generation for the reports page: pick an area and a date
/// range, get a table and a CSV. Every report is a flat, denormalized listing so it opens
/// cleanly in a spreadsheet.
/// </summary>
public class ReportService(LabelsMisDbContext db)
{
    public async Task<GeneratedReport> GenerateAsync(
        ReportType type,
        DateOnly from,
        DateOnly to,
        ReportFilters? filters = null,
        CancellationToken cancellationToken = default)
    {
        filters ??= new ReportFilters();
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var rangeLabel = $"{from:yyyy-MM-dd} to {to:yyyy-MM-dd}";

        return type switch
        {
            ReportType.Invoices => await InvoicesAsync(from, to, rangeLabel, filters, cancellationToken),
            ReportType.Payments => await PaymentsAsync(from, to, rangeLabel, filters, cancellationToken),
            ReportType.SalesOrders => await SalesOrdersAsync(fromUtc, toUtc, rangeLabel, filters, cancellationToken),
            ReportType.Estimates => await EstimatesAsync(fromUtc, toUtc, rangeLabel, filters, cancellationToken),
            ReportType.Jobs => await JobsAsync(fromUtc, toUtc, rangeLabel, filters, cancellationToken),
            ReportType.Shipments => await ShipmentsAsync(from, to, rangeLabel, filters, cancellationToken),
            ReportType.PurchaseOrders => await PurchaseOrdersAsync(fromUtc, toUtc, rangeLabel, filters, cancellationToken),
            _ => throw new InvalidOperationException("Unknown report type.")
        };
    }

    public static byte[] ToCsv(GeneratedReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', report.Columns.Select(Csv)));
        foreach (var row in report.Rows)
        {
            sb.AppendLine(string.Join(',', row.Select(Csv)));
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private async Task<GeneratedReport> InvoicesAsync(DateOnly from, DateOnly to, string rangeLabel, ReportFilters filters, CancellationToken ct)
    {
        var rows = await db.Invoices.AsNoTracking()
            .Include(i => i.Customer)
            .Where(i => i.InvoiceDate >= from && i.InvoiceDate <= to)
            .Where(i => filters.CustomerId == null || i.CustomerId == filters.CustomerId)
            .OrderBy(i => i.InvoiceDate).ThenBy(i => i.InvoiceNumber)
            .Select(i => new[]
            {
                i.InvoiceNumber,
                i.Customer.Name,
                i.InvoiceDate.ToString(),
                i.DueDate.ToString(),
                i.Status.ToString(),
                i.Subtotal.ToString(),
                i.TaxAmount.ToString(),
                i.ShippingAmount.ToString(),
                i.Total.ToString(),
                i.BalanceDue.ToString(),
                i.QbExportedAt != null ? "Yes" : "No"
            })
            .ToListAsync(ct);

        return new GeneratedReport(
            $"Invoices — {rangeLabel}",
            ["Invoice #", "Customer", "Date", "Due", "Status", "Subtotal", "Tax", "Shipping", "Total", "Balance", "Exported"],
            rows);
    }

    private async Task<GeneratedReport> PaymentsAsync(DateOnly from, DateOnly to, string rangeLabel, ReportFilters filters, CancellationToken ct)
    {
        var rows = await db.Payments.AsNoTracking()
            .Include(p => p.Invoice).ThenInclude(i => i.Customer)
            .Where(p => p.PaymentDate >= from && p.PaymentDate <= to)
            .Where(p => filters.CustomerId == null || p.Invoice.CustomerId == filters.CustomerId)
            .OrderBy(p => p.PaymentDate)
            .Select(p => new[]
            {
                p.PaymentDate.ToString(),
                p.Invoice.InvoiceNumber,
                p.Invoice.Customer.Name,
                p.Amount.ToString(),
                p.Method.ToString(),
                p.Reference ?? "",
                p.Notes ?? ""
            })
            .ToListAsync(ct);

        return new GeneratedReport(
            $"Payments received — {rangeLabel}",
            ["Date", "Invoice #", "Customer", "Amount", "Method", "Reference", "Notes"],
            rows);
    }

    private async Task<GeneratedReport> SalesOrdersAsync(DateTime fromUtc, DateTime toUtc, string rangeLabel, ReportFilters filters, CancellationToken ct)
    {
        var rows = await db.SalesOrders.AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Lines)
            .Where(o => o.OrderedAt >= fromUtc && o.OrderedAt < toUtc)
            .Where(o => filters.CustomerId == null || o.CustomerId == filters.CustomerId)
            .Where(o => filters.ProductId == null || o.Lines.Any(l => l.ProductId == filters.ProductId))
            .Where(o => filters.DieId == null || o.Lines.Any(l => l.Spec != null && l.Spec.DieId == filters.DieId))
            .OrderBy(o => o.OrderedAt)
            .Select(o => new[]
            {
                o.OrderNumber,
                o.Customer.Name,
                o.CustomerPoNumber ?? "",
                o.OrderedAt.ToString("yyyy-MM-dd"),
                o.RequestedShipDate != null ? o.RequestedShipDate.ToString()! : "",
                o.Status.ToString(),
                o.Lines.Sum(l => l.LineTotal).ToString(),
                o.ShippingCost.ToString()
            })
            .ToListAsync(ct);

        return new GeneratedReport(
            $"Sales orders — {rangeLabel}",
            ["Order #", "Customer", "Customer PO", "Ordered", "Ship date", "Status", "Lines total", "Shipping"],
            rows);
    }

    private async Task<GeneratedReport> EstimatesAsync(DateTime fromUtc, DateTime toUtc, string rangeLabel, ReportFilters filters, CancellationToken ct)
    {
        var rows = await db.Estimates.AsNoTracking()
            .Include(e => e.Customer)
            .Include(e => e.Lines).ThenInclude(l => l.QuantityBreaks)
            .Where(e => e.CreatedAt >= fromUtc && e.CreatedAt < toUtc)
            .Where(e => filters.CustomerId == null || e.CustomerId == filters.CustomerId)
            .OrderBy(e => e.CreatedAt)
            .Select(e => new[]
            {
                e.EstimateNumber,
                e.RevisionNumber.ToString(),
                e.Customer.Name,
                e.CreatedAt.ToString("yyyy-MM-dd"),
                e.Status.ToString(),
                e.ValidUntilDate != null ? e.ValidUntilDate.ToString()! : "",
                e.Lines.SelectMany(l => l.QuantityBreaks).Max(q => (decimal?)q.TotalPrice) != null
                    ? e.Lines.SelectMany(l => l.QuantityBreaks).Max(q => q.TotalPrice).ToString()
                    : ""
            })
            .ToListAsync(ct);

        return new GeneratedReport(
            $"Estimates — {rangeLabel}",
            ["Estimate #", "Rev", "Customer", "Created", "Status", "Valid until", "Max quoted total"],
            rows);
    }

    private async Task<GeneratedReport> JobsAsync(DateTime fromUtc, DateTime toUtc, string rangeLabel, ReportFilters filters, CancellationToken ct)
    {
        var rows = await db.Jobs.AsNoTracking()
            .Include(j => j.Product).ThenInclude(p => p.PrimaryCustomer)
            .Include(j => j.SalesOrderLine).ThenInclude(l => l.SalesOrder)
            .Include(j => j.Operations)
            .Where(j => j.CreatedAt >= fromUtc && j.CreatedAt < toUtc)
            .Where(j => filters.CustomerId == null || j.SalesOrderLine.SalesOrder.CustomerId == filters.CustomerId)
            .Where(j => filters.ProductId == null || j.ProductId == filters.ProductId)
            .Where(j => filters.DieId == null || (j.Spec != null && j.Spec.DieId == filters.DieId) || j.Product.DieId == filters.DieId)
            .OrderBy(j => j.CreatedAt)
            .Select(j => new[]
            {
                j.JobNumber,
                j.SalesOrderLine.SalesOrder.OrderNumber,
                j.Product.PrimaryCustomer != null ? j.Product.PrimaryCustomer.Name : "",
                j.SalesOrderLine.Description ?? j.Product.Description,
                j.Status.ToString(),
                j.DueDate != null ? j.DueDate.ToString()! : "",
                j.QuantityOrdered.ToString(),
                j.QuantityPlanned.ToString(),
                j.Operations.Sum(o => o.GoodCount).ToString(),
                j.Operations.Sum(o => o.WasteCount).ToString(),
                j.Operations.Sum(o => o.DowntimeMinutes).ToString()
            })
            .ToListAsync(ct);

        return new GeneratedReport(
            $"Jobs — {rangeLabel}",
            ["Job #", "Order #", "Customer", "Product", "Status", "Due", "Qty ordered", "Qty planned", "Good", "Waste", "Downtime (min)"],
            rows);
    }

    private async Task<GeneratedReport> ShipmentsAsync(DateOnly from, DateOnly to, string rangeLabel, ReportFilters filters, CancellationToken ct)
    {
        var rows = await db.Shipments.AsNoTracking()
            .Include(s => s.SalesOrder).ThenInclude(o => o.Customer)
            .Include(s => s.Packages)
            .Where(s => s.ShipDate >= from && s.ShipDate <= to)
            .Where(s => filters.CustomerId == null || s.SalesOrder.CustomerId == filters.CustomerId)
            .OrderBy(s => s.ShipDate)
            .Select(s => new[]
            {
                s.ShipmentNumber,
                s.SalesOrder.OrderNumber,
                s.SalesOrder.Customer.Name,
                s.ShipDate.ToString(),
                s.Status.ToString(),
                s.Carrier.ToString(),
                s.Packages.Count.ToString(),
                s.TotalShippingCost.ToString()
            })
            .ToListAsync(ct);

        return new GeneratedReport(
            $"Shipments — {rangeLabel}",
            ["Shipment #", "Order #", "Customer", "Ship date", "Status", "Carrier", "Packages", "Shipping cost"],
            rows);
    }

    private async Task<GeneratedReport> PurchaseOrdersAsync(DateTime fromUtc, DateTime toUtc, string rangeLabel, ReportFilters filters, CancellationToken ct)
    {
        var rows = await db.PurchaseOrders.AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.Lines)
            .Where(p => p.OrderedAt >= fromUtc && p.OrderedAt < toUtc)
            .Where(p => filters.SupplierId == null || p.SupplierId == filters.SupplierId)
            .OrderBy(p => p.OrderedAt)
            .Select(p => new[]
            {
                p.PoNumber,
                p.Supplier.Name,
                p.OrderedAt.ToString("yyyy-MM-dd"),
                p.ExpectedAt != null ? p.ExpectedAt.ToString()! : "",
                p.Status.ToString(),
                p.Lines.Sum(l => l.LineTotal).ToString()
            })
            .ToListAsync(ct);

        return new GeneratedReport(
            $"Purchase orders — {rangeLabel}",
            ["PO #", "Supplier", "Ordered", "Expected", "Status", "Total"],
            rows);
    }

    private static string Csv(string? value)
    {
        var text = value ?? string.Empty;
        return text.Contains('"') || text.Contains(',') || text.Contains('\n')
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;
    }
}
