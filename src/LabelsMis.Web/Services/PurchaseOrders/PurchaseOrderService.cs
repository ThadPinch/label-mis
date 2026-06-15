using LabelsMis.Domain.Email;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Pdf;
using LabelsMis.Web.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.PurchaseOrders;

public record PurchaseOrderLineInput(
    Guid? Id,
    Guid StockId,
    decimal QuantityLf);

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
    DocumentNumberService documentNumbers,
    PurchaseOrderPdfGenerator pdfGenerator,
    IEmailSender emailSender)
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
            .Include(p => p.Supplier).ThenInclude(s => s.Contacts)
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

        var lines = await BuildLinesAsync(po.Id, input.Lines, userId, now, cancellationToken);
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

        po.UpdateDraftDetails(input.ExpectedAt, input.Notes, userId, now);

        po.ReplaceLines([]);
        db.PurchaseOrderLines.RemoveRange(await db.PurchaseOrderLines.Where(l => l.PurchaseOrderId == id).ToListAsync(cancellationToken));

        var lines = await BuildLinesAsync(po.Id, input.Lines, userId, now, cancellationToken);
        foreach (var line in lines)
        {
            po.AddLine(line);
            db.PurchaseOrderLines.Add(line);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SendAsync(Guid id, bool sendEmail, string? emailTo, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;

        var detail = await GetAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Purchase order not found.");

        detail.MarkSent(userId, now);
        await db.SaveChangesAsync(cancellationToken);

        if (sendEmail && !string.IsNullOrWhiteSpace(emailTo))
        {
            var pdfPath = await pdfGenerator.GenerateAsync(detail, cancellationToken);
            await emailSender.SendAsync(
                emailTo,
                $"Purchase order {detail.PoNumber}",
                $"Hello {detail.Supplier.Name},\n\nPlease find attached purchase order {detail.PoNumber}. " +
                $"The order total is {detail.Lines.Sum(l => l.LineTotal):C2}" +
                (detail.ExpectedAt.HasValue ? $", with an expected date of {detail.ExpectedAt:MMM d, yyyy}" : string.Empty) +
                ".\n\nPlease confirm receipt and advise of any pricing or lead-time discrepancies.\n\nThank you.",
                [pdfPath],
                cancellationToken);
        }
    }

    /// <summary>Renders the PO PDF as bytes for an in-browser view (no persistence).</summary>
    public async Task<byte[]?> RenderPdfBytesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var po = await GetAsync(id, cancellationToken);
        return po is null ? null : await pdfGenerator.GenerateBytesAsync(po, cancellationToken);
    }

    public async Task DeleteDraftAsync(Guid id, CancellationToken cancellationToken = default)
    {
        RequireUserId();
        var po = await db.PurchaseOrders
            .Include(p => p.Lines)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Purchase order not found.");

        if (po.Status is not PurchaseOrderStatus.Draft)
        {
            throw new InvalidOperationException("Only draft POs can be deleted.");
        }

        db.PurchaseOrderLines.RemoveRange(po.Lines);
        db.PurchaseOrders.Remove(po);
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

    private async Task<List<PurchaseOrderLine>> BuildLinesAsync(
        Guid poId,
        IReadOnlyList<PurchaseOrderLineInput> lines,
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var stockIds = lines.Select(l => l.StockId).Distinct().ToList();
        var stocks = await db.Stocks.AsNoTracking()
            .Where(s => stockIds.Contains(s.Id))
            .Select(s => new { s.Id, s.CostPerMsi, s.WidthIn })
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        // Always assign fresh ids: edits replace the line set wholesale, and reusing an
        // existing line id while its original row is being deleted in the same SaveChanges
        // would trip EF's change tracker.
        return lines.Select((line, index) =>
        {
            var unitCost = stocks.TryGetValue(line.StockId, out var stock)
                ? DeriveUnitCostPerLf(stock.CostPerMsi, stock.WidthIn)
                : 0m;
            return PurchaseOrderLine.Create(
                Guid.NewGuid(),
                poId,
                index + 1,
                line.StockId,
                line.QuantityLf,
                unitCost,
                userId,
                now);
        }).ToList();
    }

    /// <summary>
    /// Converts a stock's cost-per-MSI (1,000 in²) into a cost per linear foot at the stock's
    /// roll width: one LF covers 12" × width in², so cost/LF = costPerMsi × (12 × width) / 1000.
    /// </summary>
    public static decimal DeriveUnitCostPerLf(decimal costPerMsi, decimal widthIn) =>
        costPerMsi * 12m * widthIn / 1000m;

    private Guid RequireUserId() =>
        currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
}
