using System.Globalization;
using System.Text;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Invoices;

public record InvoiceListItem(
    Guid Id,
    string InvoiceNumber,
    string CustomerName,
    InvoiceStatus Status,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    decimal Total,
    decimal BalanceDue,
    DateTime? QbExportedAt,
    string? ExportedBy,
    int ShippedLines,
    int TotalLines,
    Guid SalesOrderId,
    string OrderNumber)
{
    public bool IsFullyShipped => TotalLines > 0 && ShippedLines >= TotalLines;
}

public record PaymentInput(
    DateOnly PaymentDate,
    decimal Amount,
    PaymentMethod Method,
    string? Reference,
    string? Notes);

public record InvoiceDetail(
    Invoice Invoice,
    string CustomerName,
    Guid SalesOrderId,
    string OrderNumber,
    IReadOnlyList<Payment> Payments);

public record ArAgingRow(
    string CustomerName,
    decimal Current,
    decimal Days1To30,
    decimal Days31To60,
    decimal Days61To90,
    decimal Over90,
    decimal Total);

public record ArAgingReport(
    IReadOnlyList<ArAgingRow> Rows,
    decimal TotalExposure,
    DateOnly AsOfDate);

public class InvoiceOptions
{
    public const string SectionName = "Invoices";
    public decimal DefaultTaxRate { get; set; } = 0.0825m;
}

public class InvoiceService(
    LabelsMisDbContext db,
    ICurrentUserService currentUser,
    DocumentNumberService documentNumbers,
    Microsoft.Extensions.Options.IOptions<InvoiceOptions> options)
{
    public async Task<PagedResult<InvoiceListItem>> ListAsync(
        string? search,
        InvoiceStatus? status,
        Guid? customerId,
        DateOnly? fromDate,
        DateOnly? toDate,
        string? agingBucket,
        bool? exported,
        string? sort,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var query = db.Invoices.AsNoTracking()
            .Include(i => i.Customer)
            .Where(i => i.Status != InvoiceStatus.Void)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(i => i.Status == status.Value);
        }

        if (customerId.HasValue)
        {
            query = query.Where(i => i.CustomerId == customerId.Value);
        }

        if (fromDate.HasValue)
        {
            query = query.Where(i => i.InvoiceDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(i => i.InvoiceDate <= toDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(i =>
                i.InvoiceNumber.ToUpper().Contains(term)
                || i.Customer.Name.ToUpper().Contains(term));
        }

        if (exported.HasValue)
        {
            query = exported.Value
                ? query.Where(i => i.QbExportedAt != null)
                : query.Where(i => i.QbExportedAt == null);
        }

        if (!string.IsNullOrWhiteSpace(agingBucket))
        {
            var todayDay = today.DayNumber;
            query = agingBucket switch
            {
                "current" => query.Where(i => i.BalanceDue > 0 && (todayDay - i.InvoiceDate.DayNumber) <= 0),
                "1-30" => query.Where(i => i.BalanceDue > 0 && (todayDay - i.InvoiceDate.DayNumber) >= 1 && (todayDay - i.InvoiceDate.DayNumber) <= 30),
                "31-60" => query.Where(i => i.BalanceDue > 0 && (todayDay - i.InvoiceDate.DayNumber) >= 31 && (todayDay - i.InvoiceDate.DayNumber) <= 60),
                "61-90" => query.Where(i => i.BalanceDue > 0 && (todayDay - i.InvoiceDate.DayNumber) >= 61 && (todayDay - i.InvoiceDate.DayNumber) <= 90),
                "90+" => query.Where(i => i.BalanceDue > 0 && (todayDay - i.InvoiceDate.DayNumber) > 90),
                _ => query
            };
        }

        query = sort switch
        {
            "number" => query.OrderBy(i => i.InvoiceNumber),
            "customer" => query.OrderBy(i => i.Customer.Name),
            "balance" => query.OrderByDescending(i => i.BalanceDue),
            "due" => query.OrderBy(i => i.DueDate),
            _ => query.OrderByDescending(i => i.InvoiceDate)
        };

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new
            {
                i.Id,
                i.InvoiceNumber,
                CustomerName = i.Customer.Name,
                i.Status,
                i.InvoiceDate,
                i.DueDate,
                i.Total,
                i.BalanceDue,
                i.QbExportedAt,
                i.QbExportedById,
                i.SalesOrderId,
                OrderNumber = i.SalesOrder.OrderNumber
            })
            .ToListAsync(cancellationToken);

        var exporterIds = rows.Where(r => r.QbExportedById.HasValue)
            .Select(r => r.QbExportedById!.Value).Distinct().ToList();
        var exporterNames = exporterIds.Count == 0
            ? []
            : await db.Users.AsNoTracking()
                .Where(u => exporterIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Email ?? u.UserName, cancellationToken);

        var orderIds = rows.Select(r => r.SalesOrderId).Distinct().ToList();
        var lineProgress = await db.SalesOrderLines.AsNoTracking()
            .Where(l => orderIds.Contains(l.SalesOrderId))
            .Select(l => new
            {
                l.SalesOrderId,
                l.Quantity,
                Shipped = db.ShipmentLines
                    .Where(sl => sl.SalesOrderLineId == l.Id && sl.Shipment.Status != ShipmentStatus.Pending)
                    .Sum(sl => (int?)sl.QuantityShipped) ?? 0
            })
            .ToListAsync(cancellationToken);

        var progressByOrder = lineProgress
            .GroupBy(l => l.SalesOrderId)
            .ToDictionary(
                g => g.Key,
                g => (Shipped: g.Count(x => x.Shipped >= x.Quantity), Total: g.Count()));

        var items = rows.Select(r =>
        {
            var progress = progressByOrder.GetValueOrDefault(r.SalesOrderId);
            return new InvoiceListItem(
                r.Id,
                r.InvoiceNumber,
                r.CustomerName,
                r.Status,
                r.InvoiceDate,
                r.DueDate,
                r.Total,
                r.BalanceDue,
                r.QbExportedAt,
                r.QbExportedById.HasValue ? exporterNames.GetValueOrDefault(r.QbExportedById.Value) : null,
                progress.Shipped,
                progress.Total,
                r.SalesOrderId,
                r.OrderNumber);
        }).ToList();

        return new PagedResult<InvoiceListItem>(items, page, pageSize, total);
    }

    public async Task<InvoiceDetail?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await db.Invoices
            .Include(i => i.Customer)
            .Include(i => i.SalesOrder)
            .Include(i => i.Lines)
            .Include(i => i.Payments)
            .SingleOrDefaultAsync(i => i.Id == id, cancellationToken);

        return invoice is null
            ? null
            : new InvoiceDetail(
                invoice,
                invoice.Customer.Name,
                invoice.SalesOrderId,
                invoice.SalesOrder.OrderNumber,
                invoice.Payments.ToList());
    }

    /// <summary>
    /// Generates a draft invoice for the full sales order. Called automatically when a sales order
    /// is created. Idempotent: returns the existing invoice if one already exists.
    /// </summary>
    public async Task<Invoice> CreateFromSalesOrderAsync(Guid salesOrderId, CancellationToken cancellationToken = default)
    {
        var existing = await db.Invoices
            .Where(i => i.SalesOrderId == salesOrderId && i.Status != InvoiceStatus.Void)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        var order = await db.SalesOrders
            .Include(o => o.Customer)
            .Include(o => o.Lines).ThenInclude(l => l.Product)
            .SingleAsync(o => o.Id == salesOrderId, cancellationToken);

        var invoiceNumber = await documentNumbers.NextInvoiceNumberAsync(cancellationToken);
        var dueDate = today.AddDays(order.Customer.Terms.ToDueDays());
        var subtotal = order.Lines.Sum(l => l.Quantity * l.UnitPrice);
        var taxAmount = order.Customer.TaxExempt
            ? 0m
            : Math.Round(subtotal * options.Value.DefaultTaxRate, 4, MidpointRounding.AwayFromZero);
        var shippingAmount = order.ShippingCost;

        var invoice = Invoice.CreateDraft(
            Guid.NewGuid(),
            invoiceNumber,
            order.CustomerId,
            order.Id,
            null,
            today,
            dueDate,
            subtotal,
            taxAmount,
            shippingAmount,
            null,
            userId,
            now);

        var lineNumber = 1;
        foreach (var orderLine in order.Lines.OrderBy(l => l.LineNumber))
        {
            var line = InvoiceLine.Create(
                Guid.NewGuid(),
                invoice.Id,
                lineNumber++,
                orderLine.Id,
                null,
                orderLine.Description ?? orderLine.Product.Description,
                orderLine.Quantity,
                orderLine.UnitPrice,
                order.Customer.TaxExempt ? "NON" : "TAX",
                userId,
                now);
            invoice.AddLine(line);
            db.InvoiceLines.Add(line);
        }

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);
        return invoice;
    }

    public async Task<Invoice> CreateFromShipmentAsync(Guid shipmentId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        var shipment = await db.Shipments
            .Include(s => s.SalesOrder).ThenInclude(o => o.Customer)
            .Include(s => s.Lines).ThenInclude(l => l.SalesOrderLine).ThenInclude(sl => sl.Product)
            .Include(s => s.Packages)
            .SingleAsync(s => s.Id == shipmentId, cancellationToken);

        if (shipment.Status is not ShipmentStatus.InTransit and not ShipmentStatus.Delivered)
        {
            throw new InvalidOperationException("Invoice can only be generated for shipped orders.");
        }

        var existing = await db.Invoices
            .Where(i => i.SalesOrderId == shipment.SalesOrderId && i.Status != InvoiceStatus.Void)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var invoiceNumber = await documentNumbers.NextInvoiceNumberAsync(cancellationToken);
        var dueDate = today.AddDays(shipment.SalesOrder.Customer.Terms.ToDueDays());
        var subtotal = shipment.Lines.Sum(l => l.QuantityShipped * l.SalesOrderLine.UnitPrice);
        var taxAmount = shipment.SalesOrder.Customer.TaxExempt
            ? 0m
            : Math.Round(subtotal * options.Value.DefaultTaxRate, 4, MidpointRounding.AwayFromZero);
        var shippingAmount = shipment.TotalShippingCost > 0
            ? shipment.TotalShippingCost
            : shipment.Packages.Sum(p => p.ShippingCost);

        var invoice = Invoice.CreateDraft(
            Guid.NewGuid(),
            invoiceNumber,
            shipment.SalesOrder.CustomerId,
            shipment.SalesOrderId,
            shipmentId,
            today,
            dueDate,
            subtotal,
            taxAmount,
            shippingAmount,
            null,
            userId,
            now);

        var lineNumber = 1;
        foreach (var shipmentLine in shipment.Lines.OrderBy(l => l.SalesOrderLine.LineNumber))
        {
            var line = InvoiceLine.Create(
                Guid.NewGuid(),
                invoice.Id,
                lineNumber++,
                shipmentLine.SalesOrderLineId,
                shipmentLine.JobId,
                shipmentLine.SalesOrderLine.Description ?? shipmentLine.SalesOrderLine.Product.Description,
                shipmentLine.QuantityShipped,
                shipmentLine.SalesOrderLine.UnitPrice,
                shipment.SalesOrder.Customer.TaxExempt ? "NON" : "TAX",
                userId,
                now);
            invoice.AddLine(line);
            db.InvoiceLines.Add(line);
        }

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);
        return invoice;
    }

    public async Task RecordPaymentAsync(
        Guid invoiceId,
        PaymentInput input,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var invoice = await db.Invoices
            .Include(i => i.Payments)
            .SingleAsync(i => i.Id == invoiceId, cancellationToken);

        var payment = Payment.Create(
            Guid.NewGuid(),
            invoiceId,
            input.PaymentDate,
            input.Amount,
            input.Method,
            input.Reference,
            input.Notes,
            userId,
            now);

        invoice.RecordPayment(payment);
        db.Payments.Add(payment);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Marks the given invoices as exported, skipping void or already-exported ones.
    /// Returns how many were actually marked.</summary>
    public async Task<int> MarkExportedAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var invoices = await db.Invoices
            .Where(i => invoiceIds.Contains(i.Id) && i.QbExportedAt == null && i.Status != InvoiceStatus.Void)
            .ToListAsync(cancellationToken);

        foreach (var invoice in invoices)
        {
            invoice.MarkExported(now, userId, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return invoices.Count;
    }

    public async Task UnmarkExportedAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var invoice = await db.Invoices.SingleAsync(i => i.Id == invoiceId, cancellationToken);
        invoice.UnmarkExported(userId, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Global tab counts for the invoices page: non-void invoices not yet / already exported.</summary>
    public async Task<(int Ready, int Exported)> GetExportCountsAsync(CancellationToken cancellationToken = default)
    {
        var counts = await db.Invoices.AsNoTracking()
            .Where(i => i.Status != InvoiceStatus.Void)
            .GroupBy(i => i.QbExportedAt != null)
            .Select(g => new { Exported = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return (
            counts.FirstOrDefault(c => !c.Exported)?.Count ?? 0,
            counts.FirstOrDefault(c => c.Exported)?.Count ?? 0);
    }

    public async Task VoidAsync(Guid invoiceId, string reason, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var invoice = await db.Invoices.SingleAsync(i => i.Id == invoiceId, cancellationToken);
        invoice.Void(reason, userId, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ArAgingReport> GetArAgingAsync(CancellationToken cancellationToken = default)
    {
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);
        var invoices = await db.Invoices.AsNoTracking()
            .Include(i => i.Customer)
            .Where(i => i.BalanceDue > 0 && i.Status != InvoiceStatus.Void)
            .ToListAsync(cancellationToken);

        var rows = invoices
            .GroupBy(i => i.Customer.Name)
            .Select(g =>
            {
                decimal current = 0, d1 = 0, d31 = 0, d61 = 0, over90 = 0;
                foreach (var invoice in g)
                {
                    var age = GetAgeDays(invoice.InvoiceDate, asOf);
                    var amount = invoice.BalanceDue;
                    if (age <= 0) current += amount;
                    else if (age <= 30) d1 += amount;
                    else if (age <= 60) d31 += amount;
                    else if (age <= 90) d61 += amount;
                    else over90 += amount;
                }

                return new ArAgingRow(g.Key, current, d1, d31, d61, over90, current + d1 + d31 + d61 + over90);
            })
            .OrderBy(r => r.CustomerName)
            .ToList();

        return new ArAgingReport(rows, rows.Sum(r => r.Total), asOf);
    }

    public async Task<(byte[] Content, string FileName, int InvoiceCount)> ExportCsvAsync(
        DateOnly from,
        DateOnly to,
        bool forceReexport,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;

        var query = db.Invoices
            .Include(i => i.Customer)
            .Include(i => i.Lines)
            .Where(i => i.InvoiceDate >= from && i.InvoiceDate <= to && i.Status != InvoiceStatus.Void);

        if (!forceReexport)
        {
            query = query.Where(i => i.QbExportedAt == null);
        }

        var invoices = await query.OrderBy(i => i.InvoiceNumber).ToListAsync(cancellationToken);
        var content = BuildCsv(invoices);

        foreach (var invoice in invoices)
        {
            invoice.MarkExported(now, userId, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        var fileName = $"invoices-{from:yyyyMMdd}-{to:yyyyMMdd}.csv";
        return (content, fileName, invoices.Count);
    }

    /// <summary>Exports the given invoices to the QuickBooks CSV and marks them exported.
    /// Void invoices are skipped.</summary>
    public async Task<(byte[] Content, string FileName, int InvoiceCount)> ExportSelectedCsvAsync(
        IReadOnlyCollection<Guid> invoiceIds,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;

        var invoices = await db.Invoices
            .Include(i => i.Customer)
            .Include(i => i.Lines)
            .Where(i => invoiceIds.Contains(i.Id) && i.Status != InvoiceStatus.Void)
            .OrderBy(i => i.InvoiceNumber)
            .ToListAsync(cancellationToken);

        var content = BuildCsv(invoices);

        foreach (var invoice in invoices)
        {
            invoice.MarkExported(now, userId, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        var fileName = $"invoices-{now:yyyyMMdd-HHmmss}.csv";
        return (content, fileName, invoices.Count);
    }

    private static byte[] BuildCsv(IReadOnlyList<Invoice> invoices)
    {
        var sb = new StringBuilder();
        sb.AppendLine("InvoiceNo,Customer,InvoiceDate,DueDate,Item,Description,Qty,Rate,Amount,TaxAmount");

        foreach (var invoice in invoices)
        {
            foreach (var line in invoice.Lines.OrderBy(l => l.LineNumber))
            {
                sb.Append(Csv(invoice.InvoiceNumber)).Append(',')
                    .Append(Csv(invoice.Customer.Name)).Append(',')
                    .Append(invoice.InvoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                    .Append(invoice.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                    .Append(Csv(line.Description)).Append(',')
                    .Append(Csv(line.Description)).Append(',')
                    .Append(line.Quantity.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(line.UnitPrice.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(line.LineTotal.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(invoice.TaxAmount.ToString(CultureInfo.InvariantCulture))
                    .AppendLine();
            }
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static int GetAgeDays(DateOnly invoiceDate, DateOnly asOf) =>
        asOf.DayNumber - invoiceDate.DayNumber;

    private static string Csv(string? value)
    {
        var text = value ?? string.Empty;
        return text.Contains('"') || text.Contains(',')
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;
    }

    private Guid RequireUserId() =>
        currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
}
