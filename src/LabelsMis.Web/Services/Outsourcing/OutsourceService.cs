using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.Jobs;
using LabelsMis.Web.Services.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Outsourcing;

/// <summary>Where an outsourced item is in its life: derived from sent/receipt/complete stamps.</summary>
public enum OutsourceItemStatus
{
    /// <summary>Nothing has gone to the vendor yet.</summary>
    Pending = 0,
    /// <summary>Sent to the vendor, nothing back yet.</summary>
    AtVendor = 1,
    /// <summary>Some quantity received, more expected.</summary>
    PartiallyReceived = 2,
    /// <summary>Received in full (or closed out).</summary>
    Received = 3
}

/// <summary>One row on the production Outsourced page — a sales-order line (with its job) or an
/// outsourced additional charge (promo/print/wide format), with the vendor facts and tracking state.</summary>
public record OutsourcedItemRow(
    Guid Id,
    bool IsLine,
    Guid SalesOrderId,
    string OrderNumber,
    SalesOrderStatus OrderStatus,
    string CustomerName,
    Guid? JobId,
    string? JobNumber,
    string Description,
    int Quantity,
    int QuantityReceived,
    Guid? VendorId,
    string? VendorName,
    string? QuoteNumber,
    decimal? VendorCost,
    decimal Price,
    DateOnly? ExpectedIn,
    DateOnly? RequestedShipDate,
    DateTime? SentToVendorAt,
    DateTime? ReceivedAt,
    string? PrivateNotes,
    OutsourceItemStatus Status)
{
    public int QuantityRemaining => Math.Max(0, Quantity - QuantityReceived);
    public bool IsOverdue(DateOnly today) => Status != OutsourceItemStatus.Received && ExpectedIn is { } d && d < today;
    public decimal? MarginPct => VendorCost is { } cost && Price > 0 ? (Price - cost) / Price : null;
}

/// <summary>What the receive/send popup needs: the row plus its receipt history.</summary>
public record OutsourceReceiptView(DateOnly ReceivedOn, int Quantity, string? Notes);

public record OutsourceActionPanel(OutsourcedItemRow Item, IReadOnlyList<OutsourceReceiptView> Receipts);

public class OutsourceService(LabelsMisDbContext db, ICurrentUserService currentUser, JobService jobService)
{
    /// <summary>Statuses whose outsourced items still show on the production page.</summary>
    public static readonly SalesOrderStatus[] TrackedOrderStatuses =
        [SalesOrderStatus.Open, SalesOrderStatus.InProduction, SalesOrderStatus.Shipped, SalesOrderStatus.Invoiced];

    /// <summary>Active suppliers flagged as outsource vendors, plus any ids the caller must keep
    /// selectable (a vendor that was since deactivated or un-flagged on an existing item).</summary>
    public async Task<List<SelectListItem>> GetVendorOptionsAsync(
        IEnumerable<Guid?>? keepIds = null,
        CancellationToken cancellationToken = default)
    {
        var keep = (keepIds ?? []).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        return await db.Suppliers.AsNoTracking()
            .Where(s => (s.IsActive && s.IsOutsourceVendor) || keep.Contains(s.Id))
            .OrderBy(s => s.Name)
            .Select(s => new SelectListItem(s.Name, s.Id.ToString()))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Open outsourced items — the count shown on the production stage nav.</summary>
    public Task<int> CountOpenAsync(CancellationToken cancellationToken = default) =>
        OpenItems().CountAsync(cancellationToken);

    private IQueryable<OutsourcedItem> OpenItems() =>
        db.OutsourcedItems.AsNoTracking()
            .Where(o => o.ReceivedAt == null && TrackedOrderStatuses.Contains(o.SalesOrder.Status));

    /// <summary>
    /// The production Outsourced list. <paramref name="status"/>: "open" (default — everything not yet
    /// received), "pending", "atvendor", "partial", "received", or "all".
    /// </summary>
    public async Task<PagedResult<OutsourcedItemRow>> ListAsync(
        string? search,
        Guid? vendorId,
        string? status,
        bool overdueOnly,
        string? sort,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 200);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var query = db.OutsourcedItems.AsNoTracking()
            .Where(o => TrackedOrderStatuses.Contains(o.SalesOrder.Status));

        query = (status ?? "open").ToLowerInvariant() switch
        {
            "pending" => query.Where(o => o.ReceivedAt == null && o.SentToVendorAt == null && !o.Receipts.Any()),
            "atvendor" => query.Where(o => o.ReceivedAt == null && o.SentToVendorAt != null && !o.Receipts.Any()),
            "partial" => query.Where(o => o.ReceivedAt == null && o.Receipts.Any()),
            "received" => query.Where(o => o.ReceivedAt != null),
            "all" => query,
            _ => query.Where(o => o.ReceivedAt == null)
        };

        if (vendorId is { } v)
        {
            query = query.Where(o => o.VendorId == v);
        }

        if (overdueOnly)
        {
            query = query.Where(o => o.ReceivedAt == null && o.ExpectedIn != null && o.ExpectedIn < today);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(o =>
                EF.Functions.ILike(o.SalesOrder.OrderNumber, $"%{term}%")
                || EF.Functions.ILike(o.SalesOrder.Customer.Name, $"%{term}%")
                || (o.Vendor != null && EF.Functions.ILike(o.Vendor.Name, $"%{term}%"))
                || (o.QuoteNumber != null && EF.Functions.ILike(o.QuoteNumber, $"%{term}%"))
                || (o.SalesOrderLine != null && (
                    (o.SalesOrderLine.Description != null && EF.Functions.ILike(o.SalesOrderLine.Description, $"%{term}%"))
                    || EF.Functions.ILike(o.SalesOrderLine.Product.Description, $"%{term}%")))
                || (o.SalesOrderCharge != null && EF.Functions.ILike(o.SalesOrderCharge.Description, $"%{term}%"))
                || db.Jobs.Any(j => j.SalesOrderLineId == o.SalesOrderLineId && EF.Functions.ILike(j.JobNumber, $"%{term}%")));
        }

        var (sortKey, desc) = QueryExtensions.ParseSort(sort);
        query = sortKey switch
        {
            "order" => query.OrderByDir(desc, o => o.SalesOrder.OrderNumber),
            "customer" => query.OrderByDir(desc, o => o.SalesOrder.Customer.Name).ThenBy(o => o.SalesOrder.OrderNumber),
            "vendor" => query.OrderByDir(desc, o => o.Vendor != null ? o.Vendor.Name : "").ThenBy(o => o.ExpectedIn),
            "due" => query.OrderByDir(desc, o => o.SalesOrder.RequestedShipDate).ThenBy(o => o.SalesOrder.OrderNumber),
            "sent" => query.OrderByDir(desc, o => o.SentToVendorAt),
            "expected" when desc => query.OrderByDescending(o => o.ExpectedIn).ThenBy(o => o.SalesOrder.OrderNumber),
            _ => query.OrderBy(o => o.ExpectedIn == null).ThenBy(o => o.ExpectedIn).ThenBy(o => o.SalesOrder.OrderNumber)
        };

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ProjectRow())
            .ToListAsync(cancellationToken);

        return new PagedResult<OutsourcedItemRow>(items, page, pageSize, total);
    }

    public async Task<OutsourceActionPanel?> GetActionPanelAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var row = await db.OutsourcedItems.AsNoTracking()
            .Where(o => o.Id == itemId)
            .Select(ProjectRow())
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var receipts = await db.OutsourceReceipts.AsNoTracking()
            .Where(r => r.OutsourcedItemId == itemId)
            .OrderBy(r => r.ReceivedOn).ThenBy(r => r.CreatedAt)
            .Select(r => new OutsourceReceiptView(r.ReceivedOn, r.Quantity, r.Notes))
            .ToListAsync(cancellationToken);

        return new OutsourceActionPanel(row, receipts);
    }

    /// <summary>The outsourced items on one order, for the order page's side card.</summary>
    public Task<List<OutsourcedItemRow>> ListForOrderAsync(Guid salesOrderId, CancellationToken cancellationToken = default) =>
        db.OutsourcedItems.AsNoTracking()
            .Where(o => o.SalesOrderId == salesOrderId)
            .OrderBy(o => o.SalesOrderLine != null ? 0 : 1)
            .ThenBy(o => o.SalesOrderLine != null ? o.SalesOrderLine.LineNumber : o.SalesOrderCharge!.LineNumber)
            .Select(ProjectRow())
            .ToListAsync(cancellationToken);

    public async Task MarkSentAsync(Guid itemId, DateOnly? sentOn, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var item = await db.OutsourcedItems.SingleOrDefaultAsync(o => o.Id == itemId, cancellationToken)
            ?? throw new InvalidOperationException("Outsourced item not found.");

        // "Sent" is a date, not an instant: stored as UTC midnight of the chosen (or today's) date
        // and rendered as that date everywhere, so it never slips a day across time zones.
        var sentDate = sentOn ?? DateOnly.FromDateTime(DateTime.Today);
        item.MarkSent(sentDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), userId, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Records a delivery. When the item completes (explicitly, or the quantity is covered) an order
    /// line's job moves straight to ready-to-ship; a charge simply closes out.
    /// </summary>
    public async Task ReceiveAsync(
        Guid itemId,
        int quantity,
        DateOnly receivedOn,
        string? notes,
        bool markComplete,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var item = await db.OutsourcedItems
            .Include(o => o.Receipts)
            .Include(o => o.SalesOrderLine)
            .Include(o => o.SalesOrderCharge)
            .SingleOrDefaultAsync(o => o.Id == itemId, cancellationToken)
            ?? throw new InvalidOperationException("Outsourced item not found.");

        var ordered = item.SalesOrderLine?.Quantity ?? item.SalesOrderCharge?.Quantity ?? 0;
        var receipt = item.Receive(Guid.NewGuid(), receivedOn, quantity, notes, markComplete, ordered, userId, now);
        db.OutsourceReceipts.Add(receipt);

        if (item.IsComplete && item.SalesOrderLineId is { } lineId)
        {
            await jobService.ReceiveOutsourcedJobAsync(lineId, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Closes out an item that will not receive anything more (balance cancelled).</summary>
    public async Task MarkCompleteAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var item = await db.OutsourcedItems.SingleOrDefaultAsync(o => o.Id == itemId, cancellationToken)
            ?? throw new InvalidOperationException("Outsourced item not found.");

        item.MarkComplete(userId, now);
        if (item.SalesOrderLineId is { } lineId)
        {
            await jobService.ReceiveOutsourcedJobAsync(lineId, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    // Instance member so the job lookup can correlate a subquery on db.Jobs (jobs hang off the
    // order line, and only exist once the order has been released to production).
    private System.Linq.Expressions.Expression<Func<OutsourcedItem, OutsourcedItemRow>> ProjectRow() =>
        o => new OutsourcedItemRow(
            o.Id,
            o.SalesOrderLineId != null,
            o.SalesOrderId,
            o.SalesOrder.OrderNumber,
            o.SalesOrder.Status,
            o.SalesOrder.Customer.Name,
            db.Jobs.Where(j => j.SalesOrderLineId == o.SalesOrderLineId).Select(j => (Guid?)j.Id).FirstOrDefault(),
            db.Jobs.Where(j => j.SalesOrderLineId == o.SalesOrderLineId).Select(j => j.JobNumber).FirstOrDefault(),
            o.SalesOrderLine != null
                ? (o.SalesOrderLine.Description ?? o.SalesOrderLine.Product.Description)
                : o.SalesOrderCharge!.Description,
            o.SalesOrderLine != null ? o.SalesOrderLine.Quantity : o.SalesOrderCharge!.Quantity,
            o.Receipts.Sum(r => r.Quantity),
            o.VendorId,
            o.Vendor != null ? o.Vendor.Name : null,
            o.QuoteNumber,
            o.VendorCost,
            o.SalesOrderLine != null ? o.SalesOrderLine.LineTotal : o.SalesOrderCharge!.LineTotal,
            o.ExpectedIn,
            o.SalesOrder.RequestedShipDate,
            o.SentToVendorAt,
            o.ReceivedAt,
            o.PrivateNotes,
            o.ReceivedAt != null ? OutsourceItemStatus.Received
                : o.Receipts.Any() ? OutsourceItemStatus.PartiallyReceived
                : o.SentToVendorAt != null ? OutsourceItemStatus.AtVendor
                : OutsourceItemStatus.Pending);

    private Guid RequireUserId() =>
        currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
}
