using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Fedex;
using LabelsMis.Domain.Shipping;
using LabelsMis.Domain.ValueObjects;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Shipments;

public record ShipmentListItem(
    Guid Id,
    Guid SalesOrderId,
    string ShipmentNumber,
    string CustomerName,
    ShipmentStatus Status,
    DateOnly ShipDate,
    Carrier Carrier,
    string? PrimaryTrackingNumber);

public record ShipmentLineInput(
    Guid SalesOrderLineId,
    Guid? JobId,
    int QuantityShipped);

public record ShipmentPackageInput(
    decimal WeightLb,
    decimal LengthIn,
    decimal WidthIn,
    decimal HeightIn,
    decimal DeclaredValue);

public record CreateShipmentInput(
    DateOnly ShipDate,
    FedexServiceLevel ServiceLevel,
    Guid ShipToAddressId,
    BillingType BillingType,
    string? BillingAccountNumber,
    IReadOnlyList<ShipmentLineInput> Lines,
    IReadOnlyList<ShipmentPackageInput> Packages);

public record ShipmentDetail(
    Shipment Shipment,
    string CustomerName,
    string? LatestTrackingStatus);

public record ReadyJobRef(Guid JobId, string JobNumber);

public record ReadyToShipOrder(
    Guid SalesOrderId,
    string OrderNumber,
    string CustomerName,
    string? CustomerPoNumber,
    DateOnly? RequestedShipDate,
    string? ShippingMethodName,
    bool IsPickup,
    int ReadyItemCount,
    int TotalItemCount,
    IReadOnlyList<ReadyJobRef> ReadyJobs,
    /// <summary>The order's method needs the shipment recorded with packages (or it has no method):
    /// no one-click Mark shipped.</summary>
    bool RequiresShipmentRecord = false);

/// <summary>Where a job's order line stands for shipping: what the job page and the jobs list need
/// to offer Mark shipped, Record shipment, or the fix for a job marked shipped with no shipment.</summary>
public record JobShippingState(
    Guid SalesOrderId,
    string OrderNumber,
    string? ShippingMethodName,
    bool RequiresShipmentRecord,
    int QuantityOrdered,
    int QuantityShipped)
{
    public int QuantityRemaining => Math.Max(0, QuantityOrdered - QuantityShipped);

    /// <summary>Production moved the job to Shipped/Closed but nothing for its line is on a recorded
    /// shipment, so the order can't count as shipped and its invoice waits.</summary>
    public bool IsMarkedShippedWithoutShipment(JobStatus jobStatus) =>
        jobStatus >= JobStatus.Shipped && QuantityShipped == 0 && QuantityOrdered > 0;
}

/// <summary>Production tried to mark a job shipped on an order whose shipping method needs the
/// shipment recorded (packages, tracking, carrier cost) on Shipments → Record shipment.</summary>
public sealed class ShipmentRecordRequiredException(Guid salesOrderId, string message)
    : InvalidOperationException(message)
{
    public Guid SalesOrderId { get; } = salesOrderId;
}

public record ManualShipmentAddress(Guid Id, string Label);

public record ManualShipmentLineView(
    Guid SalesOrderLineId,
    int LineNumber,
    string ProductDescription,
    int QuantityOrdered,
    int QuantityShipped,
    int QuantityRemaining,
    Guid? JobId,
    bool IsReady);

public record ManualShipmentOrderView(
    Guid SalesOrderId,
    string OrderNumber,
    string CustomerName,
    ShippingAddress DefaultShipTo,
    IReadOnlyList<ManualShipmentLineView> Lines,
    string? ShippingMethodName = null,
    ShippingMethodType? ShippingMethodType = null);

/// <summary>How a shipment's billed shipping is derived: the order's method and the shop's settings.</summary>
public record ShipmentBillingContext(
    string? ShippingMethodName,
    ShippingMethodType? ShippingMethodType,
    ShippingBillingSettings Settings)
{
    public string Rule => ShippingBillingRules.Describe(ShippingMethodType, Settings);
}

/// <summary>How the shipment's order stands: per line, ordered vs shipped (non-pending shipments).</summary>
public record OrderShipProgress(
    Guid SalesOrderId,
    string OrderNumber,
    SalesOrderStatus Status,
    IReadOnlyList<OrderShipProgressLine> Lines)
{
    /// <summary>Some line shipped short and the order isn't marked shipped: offer Mark order shipped.</summary>
    public bool CanMarkShipped => Status < SalesOrderStatus.Shipped
        && Lines.Count > 0
        && Lines.All(l => l.Shipped > 0)
        && Lines.Any(l => l.Shipped < l.Ordered);
}

public record OrderShipProgressLine(int LineNumber, string Description, int Ordered, int Shipped);

public record ManualShipmentLineInput(Guid SalesOrderLineId, Guid? JobId, int QuantityShipped);

public record ManualPackageInput(
    decimal WeightLb,
    decimal LengthIn,
    decimal WidthIn,
    decimal HeightIn,
    decimal DeclaredValue,
    string TrackingNumber,
    decimal ShippingCost);

public record ManualShipmentInput(
    DateOnly ShipDate,
    Carrier Carrier,
    ShippingAddress ShipTo,
    IReadOnlyList<ManualShipmentLineInput> Lines,
    IReadOnlyList<ManualPackageInput> Packages,
    decimal? CarrierCost = null);

public class ShipmentService(
    LabelsMisDbContext db,
    ICurrentUserService currentUser,
    DocumentNumberService documentNumbers,
    IFedexClient fedexClient,
    Invoices.InvoiceService invoiceService)
{
    public async Task<PagedResult<ShipmentListItem>> ListAsync(
        string? search,
        ShipmentStatus? status,
        Guid? customerId,
        DateOnly? shipFrom,
        DateOnly? shipTo,
        string? sort,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);

        var query = db.Shipments.AsNoTracking()
            .Include(s => s.SalesOrder).ThenInclude(o => o.Customer)
            .Include(s => s.Packages)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(s => s.Status == status.Value);
        }

        if (customerId.HasValue)
        {
            query = query.Where(s => s.SalesOrder.CustomerId == customerId.Value);
        }

        if (shipFrom.HasValue)
        {
            query = query.Where(s => s.ShipDate >= shipFrom.Value);
        }

        if (shipTo.HasValue)
        {
            query = query.Where(s => s.ShipDate <= shipTo.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(s =>
                s.ShipmentNumber.ToUpper().Contains(term)
                || s.SalesOrder.Customer.Name.ToUpper().Contains(term)
                || s.Packages.Any(p => p.TrackingNumber != null && p.TrackingNumber.ToUpper().Contains(term)));
        }

        query = sort switch
        {
            "number" => query.OrderBy(s => s.ShipmentNumber),
            "status" => query.OrderBy(s => s.Status),
            _ => query.OrderByDescending(s => s.ShipDate)
        };

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new ShipmentListItem(
                s.Id,
                s.SalesOrderId,
                s.ShipmentNumber,
                s.SalesOrder.Customer.Name,
                s.Status,
                s.ShipDate,
                s.Carrier,
                s.Packages.OrderBy(p => p.PackageNumber).Select(p => p.TrackingNumber).FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return new PagedResult<ShipmentListItem>(items, page, pageSize, total);
    }

    public async Task<ShipmentDetail?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var shipment = await db.Shipments
            .Include(s => s.SalesOrder).ThenInclude(o => o.Customer)
            .Include(s => s.ShipToAddress)
            .Include(s => s.ShipFromAddress)
            .Include(s => s.Lines).ThenInclude(l => l.SalesOrderLine).ThenInclude(sl => sl.Product)
            .Include(s => s.Packages).ThenInclude(p => p.TrackingEvents)
            .AsSplitQuery()
            .SingleOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (shipment is null)
        {
            return null;
        }

        var latestEvent = shipment.Packages
            .SelectMany(p => p.TrackingEvents)
            .OrderByDescending(e => e.EventAt)
            .FirstOrDefault();

        return new ShipmentDetail(
            shipment,
            shipment.SalesOrder.Customer.Name,
            latestEvent?.StatusDescription);
    }

    public async Task<SalesOrder?> GetSalesOrderForCreateAsync(
        Guid salesOrderId,
        CancellationToken cancellationToken = default) =>
        await db.SalesOrders.AsNoTracking()
            .Include(o => o.Customer).ThenInclude(c => c.Addresses)
            .Include(o => o.Lines).ThenInclude(l => l.Product)
            .AsSplitQuery()
            .SingleOrDefaultAsync(o => o.Id == salesOrderId, cancellationToken);

    /// <summary>Sales orders that have at least one job sitting at the Rewound (ready-to-ship) stage.</summary>
    public async Task<PagedResult<ReadyToShipOrder>> GetReadyToShipAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        string? sort = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);

        var query = db.SalesOrders.AsNoTracking()
            .Where(o => db.Jobs.Any(j => j.Status == JobStatus.Rewound && j.SalesOrderLine.SalesOrderId == o.Id));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(o =>
                o.OrderNumber.ToUpper().Contains(term)
                || o.Customer.Name.ToUpper().Contains(term));
        }

        var (sortKey, desc) = QueryExtensions.ParseSort(sort);
        query = sortKey switch
        {
            "order" => query.OrderByDir(desc, o => o.OrderNumber),
            "customer" => query.OrderByDir(desc, o => o.Customer.Name),
            "po" => query.OrderByDir(desc, o => o.CustomerPoNumber),
            "method" => query.OrderByDir(desc, o => o.ShippingMethod != null ? o.ShippingMethod.Name : ""),
            "shipdate" => query.OrderByDir(desc, o => o.RequestedShipDate),
            _ => query.OrderBy(o => o.RequestedShipDate ?? DateOnly.MaxValue).ThenBy(o => o.OrderNumber)
        };

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new ReadyToShipOrder(
                o.Id,
                o.OrderNumber,
                o.Customer.Name,
                o.CustomerPoNumber,
                o.RequestedShipDate,
                o.ShippingMethod != null ? o.ShippingMethod.Name : null,
                o.ShippingMethod != null && o.ShippingMethod.MethodType == ShippingMethodType.Pickup,
                o.Lines.Count(l => db.Jobs.Any(j => j.Status == JobStatus.Rewound && j.SalesOrderLineId == l.Id)),
                o.Lines.Count,
                db.Jobs
                    .Where(j => j.Status == JobStatus.Rewound && j.SalesOrderLine.SalesOrderId == o.Id)
                    .OrderBy(j => j.JobNumber)
                    .Select(j => new ReadyJobRef(j.Id, j.JobNumber))
                    .ToList(),
                o.ShippingMethod == null || o.ShippingMethod.RequiresShipmentRecord))
            .ToListAsync(cancellationToken);

        return new PagedResult<ReadyToShipOrder>(items, page, pageSize, total);
    }

    /// <summary>Loads an order with per-line ship readiness for the manual "record shipment" form.</summary>
    public async Task<ManualShipmentOrderView?> GetOrderForManualShipmentAsync(
        Guid salesOrderId,
        CancellationToken cancellationToken = default)
    {
        var order = await db.SalesOrders.AsNoTracking()
            .Include(o => o.Customer).ThenInclude(c => c.Addresses)
            .Include(o => o.ShippingMethod)
            .Include(o => o.Lines).ThenInclude(l => l.Product)
            .AsSplitQuery()
            .SingleOrDefaultAsync(o => o.Id == salesOrderId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        var lineIds = order.Lines.Select(l => l.Id).ToList();

        var jobs = await db.Jobs.AsNoTracking()
            .Where(j => lineIds.Contains(j.SalesOrderLineId))
            .Select(j => new { j.Id, j.SalesOrderLineId, j.Status })
            .ToListAsync(cancellationToken);

        var shipped = await db.ShipmentLines.AsNoTracking()
            .Where(l => lineIds.Contains(l.SalesOrderLineId) && l.Shipment.Status != ShipmentStatus.Pending)
            .GroupBy(l => l.SalesOrderLineId)
            .Select(g => new { Id = g.Key, Qty = g.Sum(x => x.QuantityShipped) })
            .ToDictionaryAsync(x => x.Id, x => x.Qty, cancellationToken);

        var lineViews = order.Lines.OrderBy(l => l.LineNumber).Select(l =>
        {
            var job = jobs.Where(j => j.SalesOrderLineId == l.Id)
                .OrderByDescending(j => j.Status)
                .FirstOrDefault();
            var shippedQty = shipped.GetValueOrDefault(l.Id);
            var remaining = Math.Max(0, l.Quantity - shippedQty);
            // Ready: the job is at Rewound, or production already moved it to Shipped/Closed while
            // nothing for the line was ever recorded on a shipment (so the order can be put right).
            // A line that shipped short on a recorded shipment is an under-run — it is finished
            // with Mark order shipped, not shipped a second time.
            var ready = job is not null && remaining > 0
                && (job.Status == JobStatus.Rewound || (job.Status >= JobStatus.Shipped && shippedQty == 0));
            return new ManualShipmentLineView(
                l.Id, l.LineNumber, l.Product.Description, l.Quantity, shippedQty, remaining, job?.Id, ready);
        }).ToList();

        // Default the ship-to address: when the shipping method requires an address, prefer what was
        // captured on the order (else the customer's default shipping address); otherwise (pickup /
        // no-ship methods) default to our own company address from general settings. Always editable.
        var requiresAddress = order.ShippingMethod?.RequiresAddress ?? false;
        ShippingAddress defaultShipTo;
        if (requiresAddress && order.ShippingAddress.HasAddress)
        {
            defaultShipTo = order.ShippingAddress;
        }
        else if (requiresAddress)
        {
            var customerAddress = order.Customer.Addresses
                .Where(a => a.AddressType == AddressType.Shipping)
                .OrderByDescending(a => a.IsDefault)
                .FirstOrDefault();
            defaultShipTo = customerAddress is null
                ? await GetCompanyShipFromAsync(cancellationToken)
                : new ShippingAddress(order.Customer.Name, customerAddress.Street1, customerAddress.Street2,
                    customerAddress.City, customerAddress.State, customerAddress.Zip, customerAddress.Country);
        }
        else
        {
            defaultShipTo = await GetCompanyShipFromAsync(cancellationToken);
        }

        return new ManualShipmentOrderView(
            order.Id,
            order.OrderNumber,
            order.Customer.Name,
            defaultShipTo.Normalized(),
            lineViews,
            order.ShippingMethod?.Name,
            order.ShippingMethod?.MethodType);
    }

    /// <summary>
    /// Records a shipment with manually entered tracking numbers, marks it in transit immediately,
    /// advances the shipped jobs to Shipped, and rolls up the sales-order status.
    /// </summary>
    public async Task<Shipment> CreateManualShipmentAsync(
        Guid salesOrderId,
        ManualShipmentInput input,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;

        var shipLines = input.Lines.Where(l => l.QuantityShipped > 0).ToList();
        if (shipLines.Count == 0)
        {
            throw new InvalidOperationException("Select at least one item to ship.");
        }

        if (input.Packages.Count == 0)
        {
            throw new InvalidOperationException("At least one package is required.");
        }

        if (input.Packages.Any(p => string.IsNullOrWhiteSpace(p.TrackingNumber)))
        {
            throw new InvalidOperationException("Each package needs a tracking number.");
        }

        var shipFrom = await GetCompanyShipFromAsync(cancellationToken);

        var shipmentNumber = await documentNumbers.NextShipmentNumberAsync(cancellationToken);
        var declaredValue = input.Packages.Sum(p => p.DeclaredValue);
        var totalCost = input.Packages.Sum(p => p.ShippingCost);

        // The carrier's charge for the whole shipment (WWEX); left blank it falls back to the
        // per-package costs when those were typed, else stays unknown until entered later.
        var carrierCost = input.CarrierCost ?? (totalCost > 0 ? totalCost : null);

        var shipment = Shipment.CreatePending(
            Guid.NewGuid(),
            shipmentNumber,
            salesOrderId,
            input.ShipDate,
            input.Carrier,
            null,
            null,
            shipFrom,
            input.ShipTo,
            shipToAddressId: null,
            declaredValue,
            BillingType.Sender,
            null,
            userId,
            now,
            carrierCost);

        var packages = input.Packages.Select((pkg, index) =>
        {
            var package = ShipmentPackage.Create(
                Guid.NewGuid(), shipment.Id, index + 1,
                pkg.WeightLb, pkg.LengthIn, pkg.WidthIn, pkg.HeightIn, pkg.DeclaredValue,
                userId, now);
            package.SetManualTracking(pkg.TrackingNumber, pkg.ShippingCost, userId, now);
            return package;
        }).ToList();

        shipment.ReplacePackages(packages);
        foreach (var package in packages)
        {
            db.ShipmentPackages.Add(package);
        }

        var billing = await GetBillingContextAsync(salesOrderId, cancellationToken);
        shipment.ComputeBilledShipping(billing.ShippingMethodType, billing.Settings);

        foreach (var line in shipLines)
        {
            var shipmentLine = ShipmentLine.Create(
                Guid.NewGuid(), shipment.Id, line.SalesOrderLineId, line.JobId, line.QuantityShipped, userId, now);
            shipment.AddLine(shipmentLine);
            db.ShipmentLines.Add(shipmentLine);
        }

        shipment.MarkInTransit(totalCost, userId, now);
        db.Shipments.Add(shipment);

        var jobIds = shipLines.Where(l => l.JobId.HasValue).Select(l => l.JobId!.Value).Distinct().ToList();
        if (jobIds.Count > 0)
        {
            var jobs = await db.Jobs.Where(j => jobIds.Contains(j.Id)).ToListAsync(cancellationToken);
            foreach (var job in jobs.Where(j => j.Status < JobStatus.Shipped))
            {
                job.AdvanceStatus(JobStatus.Shipped, userId, now);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        // Roll up the order status after the lines are persisted, so the query sees this shipment.
        await UpdateSalesOrderStatusAsync(salesOrderId, userId, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        // The draft invoice's shipping follows the shipments' billed shipping (no SO unlock).
        await invoiceService.SyncDraftShippingAsync(salesOrderId, cancellationToken);
        return shipment;
    }

    /// <summary>A package-less shipment of the order this recent is added to rather than starting a
    /// new one, so lines marked shipped one at a time on the same day go out as one shipment — one
    /// local delivery fee, not one per line. A rolling window, so a late-afternoon hand-off doesn't
    /// split at the UTC date line.</summary>
    public static readonly TimeSpan PackagelessShipmentWindow = TimeSpan.FromHours(12);

    /// <summary>
    /// Marks an order shipped without packages or tracking (pickups and other hand-offs):
    /// records every ready line's remaining quantity on a package-less shipment (the order's one from
    /// the last <see cref="PackagelessShipmentWindow"/> when there is one), advances the jobs to
    /// Shipped, and rolls up the sales-order status. Refused when the order's method needs the
    /// shipment recorded with packages.
    /// </summary>
    public async Task<Shipment> MarkShippedAsync(Guid salesOrderId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;

        var order = await GetOrderForManualShipmentAsync(salesOrderId, cancellationToken)
            ?? throw new InvalidOperationException("Order not found.");

        if (await RequiresShipmentRecordAsync(salesOrderId, cancellationToken))
        {
            throw new ShipmentRecordRequiredException(
                salesOrderId, ShipmentRecordRequiredMessage(order.OrderNumber, order.ShippingMethodName));
        }

        var readyLines = order.Lines.Where(l => l.IsReady && l.QuantityRemaining > 0).ToList();
        if (readyLines.Count == 0)
        {
            throw new InvalidOperationException("Nothing on this order is ready to ship.");
        }

        // One-click shipping is all-or-nothing: every line must be ready (or already shipped).
        // Partial orders go through the record-shipment form where lines are picked explicitly.
        if (order.Lines.Any(l => !l.IsReady && l.QuantityRemaining > 0))
        {
            throw new InvalidOperationException("All items must be ready before the order can be marked shipped without packages.");
        }

        var shipment = await GetOrStartPackagelessShipmentAsync(salesOrderId, order.DefaultShipTo, userId, now, cancellationToken);
        foreach (var line in readyLines)
        {
            var shipmentLine = ShipmentLine.Create(
                Guid.NewGuid(), shipment.Id, line.SalesOrderLineId, line.JobId, line.QuantityRemaining, userId, now);
            shipment.AddLine(shipmentLine);
            db.ShipmentLines.Add(shipmentLine);
        }

        var billing = await GetBillingContextAsync(salesOrderId, cancellationToken);
        shipment.ComputeBilledShipping(billing.ShippingMethodType, billing.Settings);

        var jobIds = readyLines.Where(l => l.JobId.HasValue).Select(l => l.JobId!.Value).Distinct().ToList();
        if (jobIds.Count > 0)
        {
            var jobs = await db.Jobs.Where(j => jobIds.Contains(j.Id)).ToListAsync(cancellationToken);
            foreach (var job in jobs.Where(j => j.Status < JobStatus.Shipped))
            {
                job.AdvanceStatus(JobStatus.Shipped, userId, now);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        // Roll up the order status after the lines are persisted, so the query sees this shipment.
        await UpdateSalesOrderStatusAsync(salesOrderId, userId, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        // The draft invoice's shipping follows the shipments' billed shipping (no SO unlock).
        await invoiceService.SyncDraftShippingAsync(salesOrderId, cancellationToken);
        return shipment;
    }

    /// <summary>
    /// Production marked a job shipped (job page, jobs list, status dropdown). On an order whose
    /// method doesn't need a shipment record, records the job's unshipped quantity on a package-less
    /// shipment (the order's one from the last <see cref="PackagelessShipmentWindow"/> when there is
    /// one), then rolls up the order status and the draft invoice's shipping. Moving the job's own
    /// status is the caller's job. Also how a job marked shipped before this rule is put right.
    /// </summary>
    /// <returns>The shipment the job went on, or null when its line has nothing left to ship.</returns>
    /// <exception cref="ShipmentRecordRequiredException">The method needs the shipment recorded with
    /// packages, tracking and carrier cost — as does an order with no method, which bills as a
    /// carrier shipment.</exception>
    public async Task<Shipment?> RecordJobShippedAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var state = await GetJobShippingStateAsync(jobId, cancellationToken)
            ?? throw new InvalidOperationException("Job not found.");
        if (state.QuantityRemaining == 0)
        {
            return null;
        }

        if (state.RequiresShipmentRecord)
        {
            throw new ShipmentRecordRequiredException(
                state.SalesOrderId, ShipmentRecordRequiredMessage(state.OrderNumber, state.ShippingMethodName));
        }

        var lineId = await db.Jobs.AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => j.SalesOrderLineId)
            .SingleAsync(cancellationToken);
        var shipTo = (await GetOrderForManualShipmentAsync(state.SalesOrderId, cancellationToken))!.DefaultShipTo;
        var shipment = await GetOrStartPackagelessShipmentAsync(state.SalesOrderId, shipTo, userId, now, cancellationToken);
        var shipmentLine = ShipmentLine.Create(
            Guid.NewGuid(), shipment.Id, lineId, jobId, state.QuantityRemaining, userId, now);
        shipment.AddLine(shipmentLine);
        db.ShipmentLines.Add(shipmentLine);

        var billing = await GetBillingContextAsync(state.SalesOrderId, cancellationToken);
        shipment.ComputeBilledShipping(billing.ShippingMethodType, billing.Settings);
        await db.SaveChangesAsync(cancellationToken);

        // Roll up the order status after the line is persisted, so the query sees this shipment.
        await UpdateSalesOrderStatusAsync(state.SalesOrderId, userId, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await invoiceService.SyncDraftShippingAsync(state.SalesOrderId, cancellationToken);
        return shipment;
    }

    /// <summary>The job's order line against its recorded (non-pending) shipments, and whether the
    /// order's method lets Mark shipped record the shipment by itself.</summary>
    public async Task<JobShippingState?> GetJobShippingStateAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var row = await db.Jobs.AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => new
            {
                j.SalesOrderLineId,
                j.SalesOrderLine.SalesOrderId,
                j.SalesOrderLine.SalesOrder.OrderNumber,
                MethodName = j.SalesOrderLine.SalesOrder.ShippingMethod == null
                    ? null
                    : j.SalesOrderLine.SalesOrder.ShippingMethod.Name,
                RequiresRecord = j.SalesOrderLine.SalesOrder.ShippingMethod == null
                    || j.SalesOrderLine.SalesOrder.ShippingMethod.RequiresShipmentRecord,
                j.SalesOrderLine.Quantity
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var shipped = await db.ShipmentLines.AsNoTracking()
            .Where(l => l.SalesOrderLineId == row.SalesOrderLineId && l.Shipment.Status != ShipmentStatus.Pending)
            .SumAsync(l => (int?)l.QuantityShipped, cancellationToken) ?? 0;
        return new JobShippingState(
            row.SalesOrderId, row.OrderNumber, row.MethodName, row.RequiresRecord, row.Quantity, shipped);
    }

    /// <summary>Whether the order is shipped only by recording the shipment with packages: its
    /// method says so, or it has no method (billed as a carrier shipment).</summary>
    public async Task<bool> RequiresShipmentRecordAsync(Guid salesOrderId, CancellationToken cancellationToken = default) =>
        await db.SalesOrders.AsNoTracking()
            .Where(o => o.Id == salesOrderId)
            .Select(o => o.ShippingMethod == null || o.ShippingMethod.RequiresShipmentRecord)
            .SingleOrDefaultAsync(cancellationToken);

    public static string ShipmentRecordRequiredMessage(string orderNumber, string? shippingMethodName) =>
        shippingMethodName is null
            ? $"{orderNumber} has no shipping method, so it ships as a carrier shipment: it's marked shipped when the shipment is recorded with its packages, tracking and carrier cost (Shipments → Record shipment)."
            : $"{orderNumber} ships by {shippingMethodName}: it's marked shipped when the shipment is recorded with its packages, tracking and carrier cost (Shipments → Record shipment).";

    /// <summary>The order's package-less shipment from the last <see cref="PackagelessShipmentWindow"/>
    /// (tracked, lines loaded), else a new one already marked shipped without packages and added to
    /// the context.</summary>
    private async Task<Shipment> GetOrStartPackagelessShipmentAsync(
        Guid salesOrderId,
        ShippingAddress shipTo,
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var since = now - PackagelessShipmentWindow;
        var recent = await db.Shipments
            .Include(s => s.Lines)
            .Where(s => s.SalesOrderId == salesOrderId
                && s.Status == ShipmentStatus.InTransit
                && !s.Packages.Any()
                && s.CreatedAt >= since)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (recent is not null)
        {
            return recent;
        }

        var shipment = Shipment.CreatePending(
            Guid.NewGuid(),
            await documentNumbers.NextShipmentNumberAsync(cancellationToken),
            salesOrderId,
            DateOnly.FromDateTime(now),
            Carrier.Other,
            null,
            null,
            await GetCompanyShipFromAsync(cancellationToken),
            shipTo,
            shipToAddressId: null,
            totalDeclaredValue: 0m,
            BillingType.Sender,
            null,
            userId,
            now);
        shipment.MarkShippedWithoutPackages(userId, now);
        db.Shipments.Add(shipment);
        return shipment;
    }

    /// <summary>
    /// How far the order has shipped, per line, for the shipment page's Mark order shipped card.
    /// </summary>
    public async Task<OrderShipProgress?> GetOrderShipProgressAsync(
        Guid salesOrderId,
        CancellationToken cancellationToken = default)
    {
        var order = await db.SalesOrders.AsNoTracking()
            .Where(o => o.Id == salesOrderId)
            .Select(o => new { o.OrderNumber, o.Status })
            .SingleOrDefaultAsync(cancellationToken);
        if (order is null)
        {
            return null;
        }

        var shippedByLine = await ShippedByLineAsync(salesOrderId, cancellationToken);
        var lines = (await db.SalesOrderLines.AsNoTracking()
                .Where(l => l.SalesOrderId == salesOrderId)
                .OrderBy(l => l.LineNumber)
                .Select(l => new { l.Id, l.LineNumber, Description = l.Description ?? l.Product.Description, l.Quantity })
                .ToListAsync(cancellationToken))
            .Select(l => new OrderShipProgressLine(l.LineNumber, l.Description, l.Quantity, shippedByLine.GetValueOrDefault(l.Id)))
            .ToList();
        return new OrderShipProgress(salesOrderId, order.OrderNumber, order.Status, lines);
    }

    /// <summary>
    /// Marks the order Shipped although a line shipped short of its ordered quantity — an
    /// under-run, which is normal in label printing. No new shipment is created; the order's
    /// status moves to Shipped, which lets its invoice bill (E6). Every line must already have
    /// shipped something on a recorded shipment.
    /// </summary>
    public async Task MarkOrderShippedAsync(Guid salesOrderId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var order = await db.SalesOrders
            .Include(o => o.Lines)
            .SingleOrDefaultAsync(o => o.Id == salesOrderId, cancellationToken)
            ?? throw new InvalidOperationException("Order not found.");
        if (order.Status is SalesOrderStatus.Cancelled)
        {
            throw new InvalidOperationException("A cancelled order can't be marked shipped.");
        }

        if (order.Status >= SalesOrderStatus.Shipped)
        {
            return;
        }

        var shippedByLine = await ShippedByLineAsync(salesOrderId, cancellationToken);
        var nothingShipped = order.Lines
            .Where(l => shippedByLine.GetValueOrDefault(l.Id) <= 0)
            .OrderBy(l => l.LineNumber)
            .Select(l => l.LineNumber)
            .ToList();
        if (order.Lines.Count == 0)
        {
            throw new InvalidOperationException("This order has no lines to ship.");
        }

        if (nothingShipped.Count > 0)
        {
            throw new InvalidOperationException(
                $"Line {string.Join(", ", nothingShipped)} hasn't shipped anything yet — record a shipment for it first.");
        }

        order.AdvanceStatus(SalesOrderStatus.Shipped, userId, DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Dictionary<Guid, int>> ShippedByLineAsync(Guid salesOrderId, CancellationToken cancellationToken) =>
        await db.ShipmentLines.AsNoTracking()
            .Where(l => l.Shipment.SalesOrderId == salesOrderId && l.Shipment.Status != ShipmentStatus.Pending)
            .GroupBy(l => l.SalesOrderLineId)
            .Select(g => new { SalesOrderLineId = g.Key, Quantity = g.Sum(l => l.QuantityShipped) })
            .ToDictionaryAsync(x => x.SalesOrderLineId, x => x.Quantity, cancellationToken);

    /// <summary>Enters or corrects the carrier's actual charge on a recorded shipment.</summary>
    public async Task SetCarrierCostAsync(
        Guid shipmentId,
        decimal? carrierCost,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var shipment = await db.Shipments.SingleOrDefaultAsync(s => s.Id == shipmentId, cancellationToken)
            ?? throw new InvalidOperationException("Shipment not found.");
        var billing = await GetBillingContextAsync(shipment.SalesOrderId, cancellationToken);
        shipment.SetCarrierCost(carrierCost, billing.ShippingMethodType, billing.Settings, userId, DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        await invoiceService.SyncDraftShippingAsync(shipment.SalesOrderId, cancellationToken);
    }

    /// <summary>The order's shipping method and the shop's shipping-charge settings — everything
    /// billed shipping is derived from.</summary>
    public async Task<ShipmentBillingContext> GetBillingContextAsync(
        Guid salesOrderId,
        CancellationToken cancellationToken = default)
    {
        var method = await db.SalesOrders.AsNoTracking()
            .Where(o => o.Id == salesOrderId && o.ShippingMethod != null)
            .Select(o => new { o.ShippingMethod!.Name, o.ShippingMethod.MethodType })
            .FirstOrDefaultAsync(cancellationToken);
        var settings = await db.GeneralSettings.AsNoTracking()
            .Select(g => new ShippingBillingSettings(g.LocalDeliveryFee, g.CarrierCostMultiplier))
            .FirstOrDefaultAsync(cancellationToken) ?? ShippingBillingSettings.Default;
        return new ShipmentBillingContext(method?.Name, method?.MethodType, settings);
    }

    public async Task<Shipment> CreateAsync(
        Guid salesOrderId,
        CreateShipmentInput input,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;

        if (input.Packages.Count == 0)
        {
            throw new InvalidOperationException("At least one package is required.");
        }

        var order = await db.SalesOrders
            .Include(o => o.Lines)
            .SingleAsync(o => o.Id == salesOrderId, cancellationToken);

        var shipFrom = await db.Addresses.AsNoTracking()
            .Where(a => a.CustomerId == order.CustomerId && a.AddressType == AddressType.Shipping)
            .OrderByDescending(a => a.IsDefault)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("No ship-from address configured.");

        var shipToAddress = await db.Addresses.AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == input.ShipToAddressId, cancellationToken)
            ?? throw new InvalidOperationException("Ship-to address not found.");

        var shipmentNumber = await documentNumbers.NextShipmentNumberAsync(cancellationToken);
        var declaredValue = input.Packages.Sum(p => p.DeclaredValue);

        var shipment = Shipment.CreatePending(
            Guid.NewGuid(),
            shipmentNumber,
            salesOrderId,
            input.ShipDate,
            Carrier.Fedex,
            input.ServiceLevel,
            shipFrom.Id,
            new ShippingAddress(null, shipFrom.Street1, shipFrom.Street2, shipFrom.City, shipFrom.State, shipFrom.Zip, shipFrom.Country),
            new ShippingAddress(null, shipToAddress.Street1, shipToAddress.Street2, shipToAddress.City, shipToAddress.State, shipToAddress.Zip, shipToAddress.Country),
            input.ShipToAddressId,
            declaredValue,
            input.BillingType,
            input.BillingAccountNumber,
            userId,
            now);

        var packages = input.Packages.Select((pkg, index) =>
            ShipmentPackage.Create(
                Guid.NewGuid(),
                shipment.Id,
                index + 1,
                pkg.WeightLb,
                pkg.LengthIn,
                pkg.WidthIn,
                pkg.HeightIn,
                pkg.DeclaredValue,
                userId,
                now)).ToList();

        shipment.ReplacePackages(packages);
        foreach (var package in packages)
        {
            db.ShipmentPackages.Add(package);
        }

        foreach (var line in input.Lines)
        {
            var shipmentLine = ShipmentLine.Create(
                Guid.NewGuid(),
                shipment.Id,
                line.SalesOrderLineId,
                line.JobId,
                line.QuantityShipped,
                userId,
                now);
            shipment.AddLine(shipmentLine);
            db.ShipmentLines.Add(shipmentLine);
        }

        db.Shipments.Add(shipment);
        await db.SaveChangesAsync(cancellationToken);
        return shipment;
    }

    public async Task<IReadOnlyList<FedexRateOption>> GetRatesAsync(
        Guid shipmentId,
        CancellationToken cancellationToken = default)
    {
        var shipment = await db.Shipments
            .Include(s => s.ShipFromAddress)
            .Include(s => s.ShipToAddress)
            .Include(s => s.Packages)
            .SingleAsync(s => s.Id == shipmentId, cancellationToken);

        if (shipment.Packages.Count == 0)
        {
            throw new InvalidOperationException("Shipment has no packages.");
        }

        var request = BuildRateRequest(shipment);
        return await fedexClient.GetRateAsync(request, cancellationToken);
    }

    public async Task<Shipment> GenerateLabelsAsync(
        Guid shipmentId,
        FedexServiceLevel serviceLevel,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;

        var shipment = await db.Shipments
            .Include(s => s.SalesOrder).ThenInclude(o => o.Lines)
            .Include(s => s.ShipFromAddress)
            .Include(s => s.ShipToAddress)
            .Include(s => s.Packages)
            .AsSplitQuery()
            .SingleAsync(s => s.Id == shipmentId, cancellationToken);

        if (shipment.Status is not ShipmentStatus.Pending)
        {
            throw new InvalidOperationException("Labels can only be generated for pending shipments.");
        }

        var serviceCode = ToFedexServiceCode(serviceLevel);
        var totalCost = 0m;

        foreach (var package in shipment.Packages.OrderBy(p => p.PackageNumber))
        {
            var rateRequest = BuildRateRequest(shipment, package);
            var shipmentRequest = new FedexShipmentRequest(
                rateRequest,
                serviceCode,
                shipment.ShipmentNumber);

            var result = await fedexClient.CreateShipmentAsync(shipmentRequest, cancellationToken);
            package.SetLabel(result.TrackingNumber, result.LabelPath, result.ShippingCost, userId, now);
            totalCost += result.ShippingCost;
        }

        shipment.MarkInTransit(totalCost, userId, now);
        var billing = await GetBillingContextAsync(shipment.SalesOrderId, cancellationToken);
        // The labels' rated cost is the best carrier cost known so far; it stays editable on the
        // shipment page for when the carrier's bill differs.
        shipment.SetCarrierCost(
            shipment.CarrierCost ?? (totalCost > 0 ? totalCost : null),
            billing.ShippingMethodType,
            billing.Settings,
            userId,
            now);
        await UpdateSalesOrderStatusAsync(shipment.SalesOrderId, userId, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await invoiceService.SyncDraftShippingAsync(shipment.SalesOrderId, cancellationToken);
        return shipment;
    }

    private async Task UpdateSalesOrderStatusAsync(
        Guid salesOrderId,
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var order = await db.SalesOrders
            .Include(o => o.Lines)
            .SingleAsync(o => o.Id == salesOrderId, cancellationToken);

        var shippedByLine = await db.ShipmentLines.AsNoTracking()
            .Where(l => l.Shipment.SalesOrderId == salesOrderId
                && l.Shipment.Status != ShipmentStatus.Pending)
            .GroupBy(l => l.SalesOrderLineId)
            .Select(g => new { SalesOrderLineId = g.Key, Quantity = g.Sum(l => l.QuantityShipped) })
            .ToDictionaryAsync(x => x.SalesOrderLineId, x => x.Quantity, cancellationToken);

        var fullyShipped = order.Lines.All(line =>
            shippedByLine.TryGetValue(line.Id, out var shipped) && shipped >= line.Quantity);

        if (fullyShipped && order.Status < SalesOrderStatus.Shipped)
        {
            order.AdvanceStatus(SalesOrderStatus.Shipped, userId, now);
        }
        else if (order.Status == SalesOrderStatus.Open)
        {
            order.AdvanceStatus(SalesOrderStatus.InProduction, userId, now);
        }
    }

    private static FedexRateRequest BuildRateRequest(Shipment shipment, ShipmentPackage? package = null)
    {
        package ??= shipment.Packages.OrderBy(p => p.PackageNumber).First();
        var shipFrom = shipment.ShipFromSnapshot;
        if (!shipFrom.HasAddress)
        {
            throw new InvalidOperationException("Carrier rating requires a ship-from address on file.");
        }
        return new FedexRateRequest(
            ToFedexAddress(shipFrom),
            ToFedexAddress(shipment.ShipToSnapshot),
            package.WeightLb,
            package.LengthIn,
            package.WidthIn,
            package.HeightIn,
            package.DeclaredValue);
    }

    private static FedexAddress ToFedexAddress(ShippingAddress address) =>
        new(
            address.Street1 ?? string.Empty,
            address.Street2,
            address.City ?? string.Empty,
            address.State ?? string.Empty,
            address.Zip ?? string.Empty,
            address.Country ?? "US");

    private static string ToFedexServiceCode(FedexServiceLevel level) => level switch
    {
        FedexServiceLevel.FedexGround => "FEDEX_GROUND",
        FedexServiceLevel.FedexExpressSaver => "FEDEX_EXPRESS_SAVER",
        FedexServiceLevel.Fedex2Day => "FEDEX_2_DAY",
        FedexServiceLevel.FedexOvernight => "FEDEX_OVERNIGHT",
        _ => "FEDEX_GROUND"
    };

    /// <summary>Builds the ship-from snapshot from the company address in General Settings.</summary>
    private async Task<ShippingAddress> GetCompanyShipFromAsync(CancellationToken cancellationToken)
    {
        var settings = await db.GeneralSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return new ShippingAddress(
            settings?.CompanyName,
            settings?.AddressLine1,
            settings?.AddressLine2,
            settings?.City,
            settings?.State,
            settings?.Zip,
            "US");
    }

    private Guid RequireUserId() =>
        currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
}
