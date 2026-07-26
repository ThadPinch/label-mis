using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Fedex;
using LabelsMis.Domain.ValueObjects;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Shipments;

public record ShipmentListItem(
    Guid Id,
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
    IReadOnlyList<ReadyJobRef> ReadyJobs);

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
    IReadOnlyList<ManualShipmentLineView> Lines);

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
    IReadOnlyList<ManualPackageInput> Packages);

public class ShipmentService(
    LabelsMisDbContext db,
    ICurrentUserService currentUser,
    DocumentNumberService documentNumbers,
    IFedexClient fedexClient)
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
            .SingleOrDefaultAsync(o => o.Id == salesOrderId, cancellationToken);

    /// <summary>Sales orders that have at least one job sitting at the Rewound (ready-to-ship) stage.</summary>
    public async Task<PagedResult<ReadyToShipOrder>> GetReadyToShipAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
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

        query = query.OrderBy(o => o.RequestedShipDate ?? DateOnly.MaxValue).ThenBy(o => o.OrderNumber);

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
                    .ToList()))
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
            var ready = job is not null && job.Status == JobStatus.Rewound && remaining > 0;
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
            lineViews);
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
            now);

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

        return shipment;
    }

    /// <summary>
    /// Marks an order shipped without packages or tracking (pickups and other hand-offs):
    /// creates a package-less shipment for every ready line's remaining quantity, advances the
    /// jobs to Shipped, and rolls up the sales-order status.
    /// </summary>
    public async Task<Shipment> MarkShippedAsync(Guid salesOrderId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;

        var order = await GetOrderForManualShipmentAsync(salesOrderId, cancellationToken)
            ?? throw new InvalidOperationException("Order not found.");

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

        var shipFrom = await GetCompanyShipFromAsync(cancellationToken);
        var shipmentNumber = await documentNumbers.NextShipmentNumberAsync(cancellationToken);

        var shipment = Shipment.CreatePending(
            Guid.NewGuid(),
            shipmentNumber,
            salesOrderId,
            DateOnly.FromDateTime(now),
            Carrier.Other,
            null,
            null,
            shipFrom,
            order.DefaultShipTo,
            shipToAddressId: null,
            totalDeclaredValue: 0m,
            BillingType.Sender,
            null,
            userId,
            now);

        foreach (var line in readyLines)
        {
            var shipmentLine = ShipmentLine.Create(
                Guid.NewGuid(), shipment.Id, line.SalesOrderLineId, line.JobId, line.QuantityRemaining, userId, now);
            shipment.AddLine(shipmentLine);
            db.ShipmentLines.Add(shipmentLine);
        }

        shipment.MarkShippedWithoutPackages(userId, now);
        db.Shipments.Add(shipment);

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

        return shipment;
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
        await UpdateSalesOrderStatusAsync(shipment.SalesOrderId, userId, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
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
