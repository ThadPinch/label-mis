using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.PurchaseOrders;

public record PurchaseOrderLineInput(
    Guid? Id,
    Guid StockId,
    decimal QuantityLf,
    decimal UnitCost);

public record PurchaseOrderFormInput(
    Guid SupplierId,
    DateOnly? ExpectedAt,
    string? Notes,
    IReadOnlyList<PurchaseOrderLineInput> Lines);

public record PurchaseOrderListItem(
    Guid Id,
    string PoNumber,
    string SupplierName,
    PurchaseOrderStatus Status,
    DateTime OrderedAt,
    DateOnly? ExpectedAt,
    decimal OrderTotal);

public record ReceiveLineInput(
    Guid PoLineId,
    decimal QuantityLf,
    string SupplierLotNumber,
    int RollCount,
    string? Location);

public record ReceiveFormInput(IReadOnlyList<ReceiveLineInput> Lines);

public class PurchaseOrderService(
    LabelsMisDbContext db,
    ICurrentUserService currentUser,
    DocumentNumberService documentNumbers)
{
    public async Task<PagedResult<PurchaseOrderListItem>> ListAsync(
        string? search,
        PurchaseOrderStatus? status,
        Guid? supplierId,
        DateOnly? expectedFrom,
        DateOnly? expectedTo,
        string? sort,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);

        var query = db.PurchaseOrders.AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.Lines)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(p => p.Status == status.Value);
        }

        if (supplierId.HasValue)
        {
            query = query.Where(p => p.SupplierId == supplierId.Value);
        }

        if (expectedFrom.HasValue)
        {
            query = query.Where(p => p.ExpectedAt >= expectedFrom.Value);
        }

        if (expectedTo.HasValue)
        {
            query = query.Where(p => p.ExpectedAt <= expectedTo.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(p =>
                p.PoNumber.ToUpper().Contains(term)
                || p.Supplier.Name.ToUpper().Contains(term));
        }

        query = sort switch
        {
            "number" => query.OrderBy(p => p.PoNumber),
            "expected" => query.OrderBy(p => p.ExpectedAt),
            "status" => query.OrderBy(p => p.Status),
            _ => query.OrderByDescending(p => p.OrderedAt)
        };

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PurchaseOrderListItem(
                p.Id,
                p.PoNumber,
                p.Supplier.Name,
                p.Status,
                p.OrderedAt,
                p.ExpectedAt,
                p.Lines.Sum(l => l.LineTotal)))
            .ToListAsync(cancellationToken);

        return new PagedResult<PurchaseOrderListItem>(items, page, pageSize, total);
    }

    public async Task<PurchaseOrder?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.PurchaseOrders
            .Include(p => p.Supplier)
            .Include(p => p.Lines).ThenInclude(l => l.Stock)
            .Include(p => p.Lines).ThenInclude(l => l.Receipts)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<PurchaseOrder> CreateAsync(
        PurchaseOrderFormInput input,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        ValidateLines(input.Lines);

        var poNumber = await documentNumbers.NextPurchaseOrderNumberAsync(cancellationToken);
        var po = PurchaseOrder.CreateDraft(
            Guid.NewGuid(),
            poNumber,
            input.SupplierId,
            now,
            input.ExpectedAt,
            input.Notes,
            userId,
            now);

        var lines = BuildLines(po.Id, input.Lines, userId, now);
        foreach (var line in lines)
        {
            po.AddLine(line);
            db.PurchaseOrderLines.Add(line);
        }

        db.PurchaseOrders.Add(po);
        await db.SaveChangesAsync(cancellationToken);
        return po;
    }

    public async Task UpdateAsync(
        Guid id,
        PurchaseOrderFormInput input,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        ValidateLines(input.Lines);

        var po = await db.PurchaseOrders
            .Include(p => p.Lines)
            .SingleAsync(p => p.Id == id, cancellationToken);

        if (po.Status is not PurchaseOrderStatus.Draft)
        {
            throw new InvalidOperationException("Only draft POs can be edited.");
        }

        po.ReplaceLines([]);
        db.PurchaseOrderLines.RemoveRange(await db.PurchaseOrderLines.Where(l => l.PurchaseOrderId == id).ToListAsync(cancellationToken));

        var lines = BuildLines(po.Id, input.Lines, userId, now);
        foreach (var line in lines)
        {
            po.AddLine(line);
            db.PurchaseOrderLines.Add(line);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SendAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var po = await db.PurchaseOrders.SingleAsync(p => p.Id == id, cancellationToken);
        po.MarkSent(userId, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ReceiveAsync(
        Guid id,
        ReceiveFormInput input,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;

        var po = await db.PurchaseOrders
            .Include(p => p.Lines).ThenInclude(l => l.Stock)
            .SingleAsync(p => p.Id == id, cancellationToken);

        if (po.Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Cancelled)
        {
            throw new InvalidOperationException("PO must be sent before receiving.");
        }

        foreach (var receiveLine in input.Lines.Where(l => l.QuantityLf > 0))
        {
            var line = po.Lines.Single(l => l.Id == receiveLine.PoLineId);
            line.RecordReceipt(receiveLine.QuantityLf, userId, now);

            var receipt = Receipt.Create(
                Guid.NewGuid(),
                line.Id,
                now,
                receiveLine.QuantityLf,
                null,
                userId,
                now);
            line.AddReceipt(receipt);
            db.Receipts.Add(receipt);

            var rollCount = Math.Max(1, receiveLine.RollCount);
            var lengthPerRoll = receiveLine.QuantityLf / rollCount;
            for (var i = 0; i < rollCount; i++)
            {
                var suffix = rollCount > 1 ? $"-{(char)('A' + i)}" : string.Empty;
                var barcode = await documentNumbers.NextRollBarcodeAsync(cancellationToken) + suffix;
                var roll = Roll.Create(
                    Guid.NewGuid(),
                    barcode,
                    line.StockId,
                    receiveLine.SupplierLotNumber,
                    line.Stock.WidthIn,
                    lengthPerRoll,
                    now,
                    receipt.Id,
                    receiveLine.Location,
                    userId,
                    now);

                var movement = RollMovement.Create(
                    Guid.NewGuid(),
                    roll.Id,
                    RollMovementType.Receive,
                    lengthPerRoll,
                    null,
                    now,
                    "PO receipt",
                    userId,
                    now);
                roll.AddMovement(movement);
                db.RollMovements.Add(movement);
                db.Rolls.Add(roll);
            }
        }

        po.UpdateReceiptStatus(userId, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateLines(IReadOnlyList<PurchaseOrderLineInput> lines)
    {
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("At least one line is required.");
        }
    }

    private static List<PurchaseOrderLine> BuildLines(
        Guid poId,
        IReadOnlyList<PurchaseOrderLineInput> lines,
        Guid userId,
        DateTime now) =>
        lines.Select((line, index) =>
            PurchaseOrderLine.Create(
                line.Id ?? Guid.NewGuid(),
                poId,
                index + 1,
                line.StockId,
                line.QuantityLf,
                line.UnitCost,
                userId,
                now)).ToList();

    private Guid RequireUserId() =>
        currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
}
