using System.Globalization;
using System.Text;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Invoicing;
using LabelsMis.Domain.Shipping;
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
    string OrderNumber,
    bool NonBillable = false,
    string? NonBillableReason = null)
{
    public bool IsFullyShipped => TotalLines > 0 && ShippedLines >= TotalLines;
}

public record PaymentInput(
    DateOnly PaymentDate,
    decimal Amount,
    PaymentMethod Method,
    string? Reference,
    string? Notes);

public record InvoiceDocumentInfo(
    Guid Id,
    string FileName,
    long SizeBytes,
    DateTime UploadedAt);

public record InvoiceDetail(
    Invoice Invoice,
    string CustomerName,
    Guid SalesOrderId,
    string OrderNumber,
    IReadOnlyList<Payment> Payments,
    IReadOnlyList<InvoiceDocumentInfo>? Documents = null,
    LabelsMis.Domain.Shipping.InvoiceShippingResolution? Shipping = null);

public record InvoicePdfResult(string InvoiceNumber, Guid SalesOrderId, byte[] Bytes);

public enum InvoiceSyncOutcome
{
    NoInvoice,
    Updated,
    SkippedSent,
    SkippedExported
}

public record InvoiceSyncResult(InvoiceSyncOutcome Outcome, string? InvoiceNumber = null);

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
    public string PdfStoragePath { get; set; } = "./data/pdfs/invoices";
}

public class InvoiceService(
    LabelsMisDbContext db,
    ICurrentUserService currentUser,
    DocumentNumberService documentNumbers,
    Settings.GeneralSettingsService generalSettings,
    Microsoft.Extensions.Options.IOptions<InvoiceOptions> options,
    Email.DocumentEmailService documentEmail,
    Pdf.InvoicePdfGenerator pdfGenerator)
{
    /// <summary>The shop's tax rate from general settings; appsettings fallback until the
    /// settings row exists.</summary>
    private async Task<decimal> GetTaxRateAsync(CancellationToken cancellationToken) =>
        (await generalSettings.GetAsync(cancellationToken))?.TaxRate ?? options.Value.DefaultTaxRate;

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
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<Guid>? onlyIds = null,
        IReadOnlyCollection<Guid>? excludeIds = null)
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

        if (onlyIds is not null)
        {
            query = query.Where(i => onlyIds.Contains(i.Id));
        }

        if (excludeIds is not null && excludeIds.Count > 0)
        {
            query = query.Where(i => !excludeIds.Contains(i.Id));
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
            // Non-billable invoices are closed without a QuickBooks record: they sit with the
            // exported ones, never on the Ready list.
            query = exported.Value
                ? query.Where(i => i.QbExportedAt != null || i.NonBillable)
                : query.Where(i => i.QbExportedAt == null && !i.NonBillable);
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

        var (sortKey, desc) = QueryExtensions.ParseSort(sort);
        query = sortKey switch
        {
            "number" => query.OrderByDir(desc, i => i.InvoiceNumber),
            "customer" => query.OrderByDir(desc, i => i.Customer.Name),
            "order" => query.OrderByDir(desc, i => i.SalesOrder.OrderNumber),
            "date" => query.OrderByDir(desc, i => i.InvoiceDate),
            "due" => query.OrderByDir(desc, i => i.DueDate),
            "status" => query.OrderByDir(desc, i => i.Status),
            "total" => query.OrderByDir(desc, i => i.Total),
            "balance" => query.OrderByDir(desc, i => i.BalanceDue),
            "exported" => query.OrderByDir(desc, i => i.QbExportedAt),
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
                OrderNumber = i.SalesOrder.OrderNumber,
                i.NonBillable,
                i.NonBillableReason
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
                r.OrderNumber,
                r.NonBillable,
                r.NonBillableReason);
        }).ToList();

        return new PagedResult<InvoiceListItem>(items, page, pageSize, total);
    }

    /// <summary>Renders the invoice PDF for download; null when the invoice doesn't exist.</summary>
    public async Task<InvoicePdfResult?> RenderPdfAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await GetDetailAsync(id, cancellationToken);
        if (detail is null)
        {
            return null;
        }

        var bytes = await pdfGenerator.GenerateBytesAsync(detail, cancellationToken);
        return new InvoicePdfResult(detail.Invoice.InvoiceNumber, detail.SalesOrderId, bytes);
    }

    public async Task<InvoiceDetail?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await db.Invoices
            .Include(i => i.Customer).ThenInclude(c => c.Addresses)
            .Include(i => i.SalesOrder)
            .Include(i => i.Lines)
            .Include(i => i.Payments)
            .AsSplitQuery()
            .SingleOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (invoice is null)
        {
            return null;
        }

        var documents = await db.SalesOrderDocuments.AsNoTracking()
            .Where(d => d.SalesOrderId == invoice.SalesOrderId)
            .OrderBy(d => d.CreatedAt)
            .Select(d => new InvoiceDocumentInfo(d.Id, d.OriginalFileName, d.FileSizeBytes, d.CreatedAt))
            .ToListAsync(cancellationToken);

        var shipping = await ResolveShippingAsync(
            invoice.SalesOrderId,
            invoice.ShippingOverride,
            invoice.SalesOrder.ShippingCost,
            invoice.ShippingAmount,
            cancellationToken);

        return new InvoiceDetail(
            invoice,
            invoice.Customer.Name,
            invoice.SalesOrderId,
            invoice.SalesOrder.OrderNumber,
            invoice.Payments.ToList(),
            documents,
            shipping);
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
            .Include(o => o.Charges)
            .AsSplitQuery()
            .SingleAsync(o => o.Id == salesOrderId, cancellationToken);

        if (order.Status is SalesOrderStatus.Cancelled)
        {
            throw new InvalidOperationException("Cancelled orders cannot be invoiced.");
        }

        var invoiceNumber = await documentNumbers.NextInvoiceNumberAsync(cancellationToken);
        var dueDate = today.AddDays(order.Customer.Terms.ToDueDays());
        var invoiceId = Guid.NewGuid();
        var lines = BuildLinesFromOrder(invoiceId, order, userId, now);
        var subtotal = lines.Sum(l => l.LineTotal);
        var taxAmount = order.Customer.TaxExempt
            ? 0m
            : Math.Round(subtotal * await GetTaxRateAsync(cancellationToken), 4, MidpointRounding.AwayFromZero);
        var shippingAmount = (await ResolveShippingAsync(
            order.Id, null, order.ShippingCost, order.ShippingCost, cancellationToken)).Amount;

        var invoice = Invoice.CreateDraft(
            invoiceId,
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

        foreach (var line in lines)
        {
            invoice.AddLine(line);
            db.InvoiceLines.Add(line);
        }

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);
        return invoice;
    }

    /// <summary>
    /// Rebuilds the order's draft invoice from the order's current lines, charges, and shipping
    /// cost so price edits made before billing flow through. Sent, paid, or QB-exported invoices
    /// are left untouched — void them and generate a new invoice to rebill at the current prices.
    /// An invoice generated from a shipment keeps its shipped quantities and only picks up the
    /// new prices.
    /// </summary>
    public async Task<InvoiceSyncResult> SyncDraftFromSalesOrderAsync(
        Guid salesOrderId,
        CancellationToken cancellationToken = default)
    {
        var invoice = await db.Invoices
            .Include(i => i.Lines)
            .Where(i => i.SalesOrderId == salesOrderId && i.Status != InvoiceStatus.Void)
            .FirstOrDefaultAsync(cancellationToken);
        if (invoice is null)
        {
            return new InvoiceSyncResult(InvoiceSyncOutcome.NoInvoice);
        }

        if (invoice.QbExportedAt is not null)
        {
            return new InvoiceSyncResult(InvoiceSyncOutcome.SkippedExported, invoice.InvoiceNumber);
        }

        if (invoice.Status is not InvoiceStatus.Draft)
        {
            return new InvoiceSyncResult(InvoiceSyncOutcome.SkippedSent, invoice.InvoiceNumber);
        }

        var userId = RequireUserId();
        var now = DateTime.UtcNow;

        var order = await db.SalesOrders
            .Include(o => o.Customer)
            .Include(o => o.Lines).ThenInclude(l => l.Product)
            .Include(o => o.Charges)
            .AsSplitQuery()
            .SingleAsync(o => o.Id == salesOrderId, cancellationToken);

        List<InvoiceLine> lines;
        if (invoice.ShipmentId is Guid shipmentId)
        {
            var shipment = await db.Shipments
                .Include(s => s.Lines).ThenInclude(l => l.SalesOrderLine).ThenInclude(sl => sl.Product)
                .AsSplitQuery()
                .SingleAsync(s => s.Id == shipmentId, cancellationToken);
            lines = BuildLinesFromShipment(invoice.Id, shipment, order, userId, now);
        }
        else
        {
            lines = BuildLinesFromOrder(invoice.Id, order, userId, now);
        }

        // Shipping follows the same rule as a recorded shipment (E4): override, else the
        // shipments' billed shipping, else the order's quote.
        var shippingAmount = (await ResolveShippingAsync(
            order.Id, invoice.ShippingOverride, order.ShippingCost, invoice.ShippingAmount, cancellationToken)).Amount;

        var subtotal = lines.Sum(l => l.LineTotal);
        var taxAmount = order.Customer.TaxExempt
            ? 0m
            : Math.Round(subtotal * await GetTaxRateAsync(cancellationToken), 4, MidpointRounding.AwayFromZero);

        db.InvoiceLines.RemoveRange(invoice.Lines);
        invoice.ReplaceLines(lines, taxAmount, shippingAmount, userId, now);
        db.InvoiceLines.AddRange(lines);
        await db.SaveChangesAsync(cancellationToken);
        return new InvoiceSyncResult(InvoiceSyncOutcome.Updated, invoice.InvoiceNumber);
    }

    /// <summary>Builds the invoice lines for a full-order invoice: one per label line, then the
    /// order's non-label charges (die creation, design time).</summary>
    private static List<InvoiceLine> BuildLinesFromOrder(Guid invoiceId, SalesOrder order, Guid userId, DateTime now)
    {
        var lines = new List<InvoiceLine>();
        var lineNumber = 1;
        foreach (var orderLine in order.Lines.OrderBy(l => l.LineNumber))
        {
            lines.Add(InvoiceLine.Create(
                Guid.NewGuid(),
                invoiceId,
                lineNumber++,
                orderLine.Id,
                null,
                orderLine.Description ?? orderLine.Product.Description,
                orderLine.Quantity,
                orderLine.UnitPrice,
                order.Customer.TaxExempt ? "NON" : "TAX",
                userId,
                now));
        }

        AppendChargeLines(lines, invoiceId, order, ref lineNumber, userId, now);
        return lines;
    }

    /// <summary>Builds the invoice lines for a shipment-born invoice: shipped quantities at the
    /// order's current prices, then the order's non-label charges.</summary>
    private static List<InvoiceLine> BuildLinesFromShipment(
        Guid invoiceId,
        Shipment shipment,
        SalesOrder order,
        Guid userId,
        DateTime now)
    {
        var lines = new List<InvoiceLine>();
        var lineNumber = 1;
        foreach (var shipmentLine in shipment.Lines.OrderBy(l => l.SalesOrderLine.LineNumber))
        {
            lines.Add(InvoiceLine.Create(
                Guid.NewGuid(),
                invoiceId,
                lineNumber++,
                shipmentLine.SalesOrderLineId,
                shipmentLine.JobId,
                shipmentLine.SalesOrderLine.Description ?? shipmentLine.SalesOrderLine.Product.Description,
                shipmentLine.QuantityShipped,
                shipmentLine.SalesOrderLine.UnitPrice,
                order.Customer.TaxExempt ? "NON" : "TAX",
                userId,
                now));
        }

        AppendChargeLines(lines, invoiceId, order, ref lineNumber, userId, now);
        return lines;
    }

    /// <summary>Appends the order's non-label charges (die creation, design time) as invoice lines.</summary>
    private static void AppendChargeLines(
        List<InvoiceLine> lines,
        Guid invoiceId,
        SalesOrder order,
        ref int lineNumber,
        Guid userId,
        DateTime now)
    {
        foreach (var charge in order.Charges.OrderBy(c => c.LineNumber))
        {
            lines.Add(InvoiceLine.Create(
                Guid.NewGuid(),
                invoiceId,
                lineNumber++,
                null,
                null,
                charge.Description,
                charge.Quantity,
                charge.UnitPrice,
                order.Customer.TaxExempt ? "NON" : "TAX",
                userId,
                now));
        }
    }

    public async Task<Invoice> CreateFromShipmentAsync(Guid shipmentId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        var shipment = await db.Shipments
            .Include(s => s.SalesOrder).ThenInclude(o => o.Customer)
            .Include(s => s.SalesOrder).ThenInclude(o => o.Charges)
            .Include(s => s.Lines).ThenInclude(l => l.SalesOrderLine).ThenInclude(sl => sl.Product)
            .AsSplitQuery()
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
        // Order-level charges (die, design) are billed on the order's single invoice, so they
        // ride along whichever path creates it first.
        var invoiceId = Guid.NewGuid();
        var lines = BuildLinesFromShipment(invoiceId, shipment, shipment.SalesOrder, userId, now);
        var subtotal = lines.Sum(l => l.LineTotal);
        var taxAmount = shipment.SalesOrder.Customer.TaxExempt
            ? 0m
            : Math.Round(subtotal * await GetTaxRateAsync(cancellationToken), 4, MidpointRounding.AwayFromZero);
        var shippingAmount = (await ResolveShippingAsync(
            shipment.SalesOrderId,
            null,
            shipment.SalesOrder.ShippingCost,
            shipment.SalesOrder.ShippingCost,
            cancellationToken)).Amount;

        var invoice = Invoice.CreateDraft(
            invoiceId,
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

        foreach (var line in lines)
        {
            invoice.AddLine(line);
            db.InvoiceLines.Add(line);
        }

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);
        return invoice;
    }

    /// <summary>
    /// Re-derives the shipping on the order's draft invoice after a shipment is recorded, shipped
    /// or its carrier cost changes — no sales-order unlock involved. Only the shipping amount (and
    /// so the total and balance) moves. Sent, paid and QB-exported invoices are never rewritten.
    /// </summary>
    public async Task<InvoiceSyncResult> SyncDraftShippingAsync(
        Guid salesOrderId,
        CancellationToken cancellationToken = default)
    {
        var invoice = await db.Invoices
            .Include(i => i.SalesOrder)
            .Where(i => i.SalesOrderId == salesOrderId && i.Status != InvoiceStatus.Void)
            .FirstOrDefaultAsync(cancellationToken);
        if (invoice is null)
        {
            return new InvoiceSyncResult(InvoiceSyncOutcome.NoInvoice);
        }

        if (invoice.QbExportedAt is not null)
        {
            return new InvoiceSyncResult(InvoiceSyncOutcome.SkippedExported, invoice.InvoiceNumber);
        }

        if (invoice.Status is not InvoiceStatus.Draft)
        {
            return new InvoiceSyncResult(InvoiceSyncOutcome.SkippedSent, invoice.InvoiceNumber);
        }

        var resolution = await ResolveShippingAsync(
            salesOrderId, invoice.ShippingOverride, invoice.SalesOrder.ShippingCost, invoice.ShippingAmount, cancellationToken);
        invoice.ApplyShipping(resolution.Amount, RequireUserId(), DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return new InvoiceSyncResult(InvoiceSyncOutcome.Updated, invoice.InvoiceNumber);
    }

    /// <summary>Sets or (with null) clears the manual shipping override on a draft invoice, then
    /// re-applies the resolved shipping.</summary>
    public async Task SetShippingOverrideAsync(
        Guid invoiceId,
        decimal? shippingOverride,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var invoice = await db.Invoices
            .Include(i => i.SalesOrder)
            .SingleOrDefaultAsync(i => i.Id == invoiceId, cancellationToken)
            ?? throw new InvalidOperationException("Invoice not found.");

        invoice.SetShippingOverride(shippingOverride, userId, now);
        var resolution = await ResolveShippingAsync(
            invoice.SalesOrderId, invoice.ShippingOverride, invoice.SalesOrder.ShippingCost, invoice.ShippingAmount, cancellationToken);
        invoice.ApplyShipping(resolution.Amount, userId, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The shipping a draft invoice for this order should carry (<see cref="InvoiceShippingRules"/>):
    /// the override; else the order's shipped shipments' billed shipping; else the quote. Shipments
    /// recorded before billed shipping existed are derived on the fly from the order's method and
    /// their carrier cost (or package costs), exactly as a new shipment would be.
    /// </summary>
    private async Task<InvoiceShippingResolution> ResolveShippingAsync(
        Guid salesOrderId,
        decimal? manualOverride,
        decimal quotedShipping,
        decimal currentAmount,
        CancellationToken cancellationToken)
    {
        if (manualOverride is not null)
        {
            return InvoiceShippingRules.Resolve(manualOverride, quotedShipping, currentAmount, []);
        }

        var shipped = await db.Shipments.AsNoTracking()
            .Where(s => s.SalesOrderId == salesOrderId && s.Status != ShipmentStatus.Pending)
            .OrderBy(s => s.ShipmentNumber)
            .Select(s => new { s.ShipmentNumber, s.BilledShipping, s.CarrierCost, s.TotalShippingCost })
            .ToListAsync(cancellationToken);

        List<ShippedShipmentBilling> billing;
        if (shipped.All(s => s.BilledShipping is not null))
        {
            billing = shipped.Select(s => new ShippedShipmentBilling(s.ShipmentNumber, s.BilledShipping)).ToList();
        }
        else
        {
            var methodType = await db.SalesOrders.AsNoTracking()
                .Where(o => o.Id == salesOrderId)
                .Select(o => o.ShippingMethod == null ? (ShippingMethodType?)null : o.ShippingMethod.MethodType)
                .FirstOrDefaultAsync(cancellationToken);
            var settings = await LoadShippingBillingSettingsAsync(cancellationToken);
            billing = shipped
                .Select(s => DeriveBilling(s.ShipmentNumber, s.BilledShipping, s.CarrierCost, s.TotalShippingCost, methodType, settings))
                .ToList();
        }

        return InvoiceShippingRules.Resolve(null, quotedShipping, currentAmount, billing);
    }

    private async Task<ShippingBillingSettings> LoadShippingBillingSettingsAsync(CancellationToken cancellationToken) =>
        await db.GeneralSettings.AsNoTracking()
            .Select(g => new ShippingBillingSettings(g.LocalDeliveryFee, g.CarrierCostMultiplier))
            .FirstOrDefaultAsync(cancellationToken) ?? ShippingBillingSettings.Default;

    /// <summary>A shipped shipment's billed shipping; shipments recorded before billed shipping
    /// existed are derived from the order's method and their carrier (or package) cost.</summary>
    private static ShippedShipmentBilling DeriveBilling(
        string shipmentNumber,
        decimal? billedShipping,
        decimal? carrierCost,
        decimal totalShippingCost,
        ShippingMethodType? methodType,
        ShippingBillingSettings settings) =>
        new(shipmentNumber, billedShipping ?? ShippingBillingRules.BilledShipping(
            methodType,
            carrierCost ?? (totalShippingCost > 0 ? totalShippingCost : null),
            settings));

    /// <summary>
    /// Why each unexported, billable invoice is not ready for QuickBooks yet
    /// (<see cref="InvoiceReadinessRules"/>); invoices with no reasons are Ready. Computed in
    /// memory over the unexported set, which stays small.
    /// </summary>
    public Task<Dictionary<Guid, IReadOnlyList<string>>> GetWaitingReasonsAsync(
        CancellationToken cancellationToken = default) =>
        ComputeWaitingReasonsAsync(null, cancellationToken);

    /// <summary>One invoice's waiting reasons; empty when it is Ready — or when it is exported,
    /// non-billable or void, which callers check separately.</summary>
    public async Task<IReadOnlyList<string>> GetWaitingReasonsAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default) =>
        (await ComputeWaitingReasonsAsync([invoiceId], cancellationToken)).GetValueOrDefault(invoiceId) ?? [];

    /// <summary>Ids of the given invoices (all unexported ones when null) that are Waiting: bulk
    /// Mark exported and the CSV exports skip these and act only on the Ready list.</summary>
    private async Task<HashSet<Guid>> WaitingIdsAsync(
        IReadOnlyCollection<Guid>? invoiceIds,
        CancellationToken cancellationToken) =>
        (await ComputeWaitingReasonsAsync(invoiceIds, cancellationToken)).Keys.ToHashSet();

    private async Task<Dictionary<Guid, IReadOnlyList<string>>> ComputeWaitingReasonsAsync(
        IReadOnlyCollection<Guid>? invoiceIds,
        CancellationToken cancellationToken)
    {
        var candidates = await db.Invoices.AsNoTracking()
            .Where(i => i.Status != InvoiceStatus.Void && i.QbExportedAt == null && !i.NonBillable)
            .Where(i => invoiceIds == null || invoiceIds.Contains(i.Id))
            .Select(i => new
            {
                i.Id,
                i.SalesOrderId,
                i.CustomerId,
                i.Status,
                i.Total,
                i.BillBeforeShip,
                i.ShippingOverride,
                i.ShippingAmount,
                Quoted = i.SalesOrder.ShippingCost,
                OrderStatus = i.SalesOrder.Status,
                MethodType = i.SalesOrder.ShippingMethod == null ? (ShippingMethodType?)null : i.SalesOrder.ShippingMethod.MethodType
            })
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return [];
        }

        var orderIds = candidates.Select(c => c.SalesOrderId).Distinct().ToList();
        var progress = (await db.SalesOrderLines.AsNoTracking()
                .Where(l => orderIds.Contains(l.SalesOrderId))
                .Select(l => new
                {
                    l.SalesOrderId,
                    l.Quantity,
                    Shipped = db.ShipmentLines
                        .Where(sl => sl.SalesOrderLineId == l.Id && sl.Shipment.Status != ShipmentStatus.Pending)
                        .Sum(sl => (int?)sl.QuantityShipped) ?? 0
                })
                .ToListAsync(cancellationToken))
            .GroupBy(l => l.SalesOrderId)
            .ToDictionary(g => g.Key, g => (
                Shipped: g.Count(x => x.Shipped >= x.Quantity),
                Total: g.Count(),
                Any: g.Any(x => x.Shipped > 0)));
        var shipments = (await db.Shipments.AsNoTracking()
                .Where(s => orderIds.Contains(s.SalesOrderId) && s.Status != ShipmentStatus.Pending)
                .OrderBy(s => s.ShipmentNumber)
                .Select(s => new { s.SalesOrderId, s.ShipmentNumber, s.BilledShipping, s.CarrierCost, s.TotalShippingCost })
                .ToListAsync(cancellationToken))
            .ToLookup(s => s.SalesOrderId);
        var settings = await LoadShippingBillingSettingsAsync(cancellationToken);
        var customerBlockers = await GetCustomerBlockersAsync(
            candidates.Select(c => c.CustomerId).Distinct().ToList(), cancellationToken);

        var result = new Dictionary<Guid, IReadOnlyList<string>>();
        foreach (var c in candidates)
        {
            var (shippedLines, totalLines, anyShipped) = progress.GetValueOrDefault(c.SalesOrderId);
            IReadOnlyList<string> pending = [];
            if (c.Status == InvoiceStatus.Draft && c.ShippingOverride is null)
            {
                var billing = shipments[c.SalesOrderId]
                    .Select(s => DeriveBilling(s.ShipmentNumber, s.BilledShipping, s.CarrierCost, s.TotalShippingCost, c.MethodType, settings))
                    .ToList();
                pending = InvoiceShippingRules.Resolve(null, c.Quoted, c.ShippingAmount, billing).PendingShipmentNumbers;
            }

            var reasons = InvoiceReadinessRules.WaitingReasons(new InvoiceReadinessFacts(
                c.BillBeforeShip,
                c.OrderStatus is SalesOrderStatus.Shipped or SalesOrderStatus.Invoiced or SalesOrderStatus.Closed,
                shippedLines,
                totalLines,
                anyShipped,
                c.Total,
                pending,
                customerBlockers.GetValueOrDefault(c.CustomerId) ?? []));
            if (reasons.Count > 0)
            {
                result[c.Id] = reasons;
            }
        }

        return result;
    }

    /// <summary>Customer-level reasons an invoice can't go to QuickBooks yet (filled in by E7).</summary>
    private Task<Dictionary<Guid, IReadOnlyList<string>>> GetCustomerBlockersAsync(
        IReadOnlyCollection<Guid> customerIds,
        CancellationToken cancellationToken) =>
        Task.FromResult(new Dictionary<Guid, IReadOnlyList<string>>());

    /// <summary>Flags the invoice ready for QuickBooks before the order ships (deposits, prepay).</summary>
    public async Task SetBillBeforeShipAsync(
        Guid invoiceId,
        bool billBeforeShip,
        CancellationToken cancellationToken = default)
    {
        var invoice = await db.Invoices.SingleOrDefaultAsync(i => i.Id == invoiceId, cancellationToken)
            ?? throw new InvalidOperationException("Invoice not found.");
        invoice.SetBillBeforeShip(billBeforeShip, RequireUserId(), DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Closes the invoice without a QuickBooks record (reason required), or reopens it.</summary>
    public async Task SetNonBillableAsync(
        Guid invoiceId,
        bool nonBillable,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var invoice = await db.Invoices.SingleOrDefaultAsync(i => i.Id == invoiceId, cancellationToken)
            ?? throw new InvalidOperationException("Invoice not found.");
        if (nonBillable)
        {
            invoice.MarkNonBillable(reason ?? string.Empty, userId, now);
        }
        else
        {
            invoice.ClearNonBillable(userId, now);
        }

        await db.SaveChangesAsync(cancellationToken);
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

    /// <summary>Emails the invoice to the customer, attaching a freshly rendered PDF. A draft
    /// invoice is marked Sent; re-sending an already-sent invoice just emails it again.</summary>
    public async Task SendAsync(
        Guid invoiceId,
        string? emailTo,
        string? subject,
        string? body,
        bool includePdf,
        bool ccMe = false,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        if (string.IsNullOrWhiteSpace(emailTo))
        {
            throw new InvalidOperationException("An email address is required to send the invoice.");
        }

        var detail = await GetDetailAsync(invoiceId, cancellationToken)
            ?? throw new InvalidOperationException("Invoice not found.");
        if (detail.Invoice.Status == InvoiceStatus.Void)
        {
            throw new InvalidOperationException("A void invoice cannot be sent.");
        }

        var pdfPath = await pdfGenerator.GenerateAsync(detail, cancellationToken);

        await documentEmail.SendAsync(
            EmailDocumentType.Invoice,
            detail.Invoice.Id,
            detail.Invoice.SalesOrderId,
            emailTo,
            string.IsNullOrWhiteSpace(subject) ? $"Invoice {detail.Invoice.InvoiceNumber}" : subject,
            string.IsNullOrWhiteSpace(body) ? $"Please find attached invoice {detail.Invoice.InvoiceNumber}." : body,
            includePdf ? [pdfPath] : null,
            ccMe,
            cancellationToken);

        var tracked = await db.Invoices.SingleAsync(i => i.Id == invoiceId, cancellationToken);
        if (tracked.Status == InvoiceStatus.Draft)
        {
            tracked.MarkSent(pdfPath, userId, DateTime.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Marks the given invoices as exported, skipping void, non-billable, already-exported
    /// and Waiting ones (bulk export acts only on the Ready list). Returns how many were marked.</summary>
    public async Task<int> MarkExportedAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var waiting = await WaitingIdsAsync(invoiceIds, cancellationToken);
        var invoices = (await db.Invoices
            .Where(i => invoiceIds.Contains(i.Id) && i.QbExportedAt == null && i.Status != InvoiceStatus.Void && !i.NonBillable)
            .ToListAsync(cancellationToken))
            .Where(i => !waiting.Contains(i.Id))
            .ToList();

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

    /// <summary>Global tab counts for the invoices page. Ready + Waiting = non-void, billable
    /// invoices not yet exported; Exported = exported or closed as non-billable. Pass the
    /// waiting set when the caller already computed it.</summary>
    public async Task<(int Ready, int Waiting, int Exported)> GetExportCountsAsync(
        IReadOnlyDictionary<Guid, IReadOnlyList<string>>? waitingReasons = null,
        CancellationToken cancellationToken = default)
    {
        var counts = await db.Invoices.AsNoTracking()
            .Where(i => i.Status != InvoiceStatus.Void)
            .GroupBy(i => i.QbExportedAt != null || i.NonBillable)
            .Select(g => new { Exported = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var waiting = (waitingReasons ?? await ComputeWaitingReasonsAsync(null, cancellationToken)).Count;

        return (
            (counts.FirstOrDefault(c => !c.Exported)?.Count ?? 0) - waiting,
            waiting,
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
            .Where(i => i.BalanceDue > 0 && i.Status != InvoiceStatus.Void && !i.NonBillable)
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
            .Where(i => i.InvoiceDate >= from && i.InvoiceDate <= to && i.Status != InvoiceStatus.Void && !i.NonBillable);

        if (!forceReexport)
        {
            query = query.Where(i => i.QbExportedAt == null);
        }

        // Unexported invoices go only from the Ready list; re-exports of exported ones are unaffected.
        var waiting = await WaitingIdsAsync(null, cancellationToken);
        var invoices = (await query.OrderBy(i => i.InvoiceNumber).ToListAsync(cancellationToken))
            .Where(i => !waiting.Contains(i.Id))
            .ToList();
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
    /// Void, non-billable and Waiting invoices are skipped (only the Ready list exports).</summary>
    public async Task<(byte[] Content, string FileName, int InvoiceCount)> ExportSelectedCsvAsync(
        IReadOnlyCollection<Guid> invoiceIds,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;

        var waiting = await WaitingIdsAsync(invoiceIds, cancellationToken);
        var invoices = (await db.Invoices
            .Include(i => i.Customer)
            .Include(i => i.Lines)
            .Where(i => invoiceIds.Contains(i.Id) && i.Status != InvoiceStatus.Void && !i.NonBillable)
            .OrderBy(i => i.InvoiceNumber)
            .ToListAsync(cancellationToken))
            .Where(i => !waiting.Contains(i.Id))
            .ToList();

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
