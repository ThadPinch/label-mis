using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Fedex;
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
        return new FedexRateRequest(
            ToFedexAddress(shipment.ShipFromAddress),
            ToFedexAddress(shipment.ShipToAddress),
            package.WeightLb,
            package.LengthIn,
            package.WidthIn,
            package.HeightIn,
            package.DeclaredValue);
    }

    private static FedexAddress ToFedexAddress(Address address) =>
        new(
            address.Street1,
            address.Street2,
            address.City,
            address.State,
            address.Zip,
            address.Country);

    private static string ToFedexServiceCode(FedexServiceLevel level) => level switch
    {
        FedexServiceLevel.FedexGround => "FEDEX_GROUND",
        FedexServiceLevel.FedexExpressSaver => "FEDEX_EXPRESS_SAVER",
        FedexServiceLevel.Fedex2Day => "FEDEX_2_DAY",
        FedexServiceLevel.FedexOvernight => "FEDEX_OVERNIGHT",
        _ => "FEDEX_GROUND"
    };

    private Guid RequireUserId() =>
        currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
}
