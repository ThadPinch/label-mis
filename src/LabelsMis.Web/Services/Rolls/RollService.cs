using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Rolls;

public record RollListItem(
    Guid Id,
    string RollBarcode,
    string StockDescription,
    string SupplierLotNumber,
    decimal WidthIn,
    decimal RemainingLengthLf,
    string? Location,
    RollStatus Status);

public record RollSplitInput(IReadOnlyList<decimal> ChildWidthsIn, string? Location);

public record ManualRollInput(
    Guid StockId,
    string SupplierLotNumber,
    decimal WidthIn,
    decimal LengthLf,
    string? Location);

public record RollDetail(
    Roll Roll,
    string StockDescription,
    IReadOnlyList<RollMovement> Movements);

public class RollService(
    LabelsMisDbContext db,
    ICurrentUserService currentUser,
    DocumentNumberService documentNumbers)
{
    public async Task<PagedResult<RollListItem>> ListAsync(
        string? search,
        Guid? stockId,
        RollStatus? status,
        string? location,
        string? lotNumber,
        StockType? stockType,
        string? sort,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);

        var query = db.Rolls.AsNoTracking()
            .Include(r => r.Stock)
            .AsQueryable();

        if (stockId.HasValue)
        {
            query = query.Where(r => r.StockId == stockId.Value);
        }

        if (stockType.HasValue)
        {
            query = query.Where(r => r.Stock.StockType == stockType.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(r => r.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(location))
        {
            query = query.Where(r => r.Location != null && r.Location.Contains(location));
        }

        if (!string.IsNullOrWhiteSpace(lotNumber))
        {
            query = query.Where(r => r.SupplierLotNumber.Contains(lotNumber));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(r =>
                r.RollBarcode.ToUpper().Contains(term)
                || r.SupplierLotNumber.ToUpper().Contains(term));
        }

        var (sortKey, desc) = QueryExtensions.ParseSort(sort);
        query = sortKey switch
        {
            "barcode" => query.OrderByDir(desc, r => r.RollBarcode),
            "stock" => query.OrderByDir(desc, r => r.Stock.Description),
            "lot" => query.OrderByDir(desc, r => r.SupplierLotNumber),
            "width" => query.OrderByDir(desc, r => r.WidthIn),
            "remaining" => query.OrderByDir(desc, r => r.RemainingLengthLf),
            "location" => query.OrderByDir(desc, r => r.Location),
            "status" => query.OrderByDir(desc, r => r.Status),
            _ => query.OrderByDescending(r => r.ReceivedAt)
        };

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new RollListItem(
                r.Id,
                r.RollBarcode,
                r.Stock.Description,
                r.SupplierLotNumber,
                r.WidthIn,
                r.RemainingLengthLf,
                r.Location,
                r.Status))
            .ToListAsync(cancellationToken);

        return new PagedResult<RollListItem>(items, page, pageSize, total);
    }

    public async Task<Guid> AddManualAsync(ManualRollInput input, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;

        var barcode = await documentNumbers.NextRollBarcodeAsync(cancellationToken);
        var roll = Roll.Create(
            Guid.NewGuid(),
            barcode,
            input.StockId,
            input.SupplierLotNumber,
            input.WidthIn,
            input.LengthLf,
            now,
            null,
            input.Location,
            userId,
            now);

        var movement = RollMovement.Create(
            Guid.NewGuid(),
            roll.Id,
            RollMovementType.Receive,
            input.LengthLf,
            null,
            now,
            "Manual inventory add",
            userId,
            now);
        roll.AddMovement(movement);
        db.RollMovements.Add(movement);
        db.Rolls.Add(roll);

        await db.SaveChangesAsync(cancellationToken);
        return roll.Id;
    }

    public async Task<RollDetail?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var roll = await db.Rolls
            .Include(r => r.Stock)
            .Include(r => r.Movements)
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

        return roll is null
            ? null
            : new RollDetail(roll, roll.Stock.Description, roll.Movements.OrderByDescending(m => m.MovedAt).ToList());
    }

    public async Task StageAsync(Guid id, string location, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var roll = await db.Rolls.SingleAsync(r => r.Id == id, cancellationToken);
        roll.Stage(location, userId, now);

        var movement = RollMovement.Create(
            Guid.NewGuid(), id, RollMovementType.Stage, 0, null, now, location, userId, now);
        db.RollMovements.Add(movement);
        roll.AddMovement(movement);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ScrapAsync(Guid id, string? notes, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var roll = await db.Rolls.SingleAsync(r => r.Id == id, cancellationToken);
        var remaining = roll.RemainingLengthLf;
        roll.Scrap(userId, now);

        var movement = RollMovement.Create(
            Guid.NewGuid(), id, RollMovementType.Scrap, -remaining, null, now, notes, userId, now);
        db.RollMovements.Add(movement);
        roll.AddMovement(movement);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SplitAsync(Guid id, RollSplitInput input, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var roll = await db.Rolls
            .Include(r => r.Stock)
            .SingleAsync(r => r.Id == id, cancellationToken);

        if (input.ChildWidthsIn.Count < 2)
        {
            throw new InvalidOperationException("Split requires at least two child widths.");
        }

        var totalChildWidth = input.ChildWidthsIn.Sum();
        if (totalChildWidth != roll.WidthIn)
        {
            throw new InvalidOperationException("Child widths must sum to the original roll width.");
        }

        var remaining = roll.RemainingLengthLf;
        roll.MarkDepleted(userId, now);
        var splitMovement = RollMovement.Create(
            Guid.NewGuid(), id, RollMovementType.Split, -remaining, null, now, "Split into child rolls", userId, now);
        db.RollMovements.Add(splitMovement);
        roll.AddMovement(splitMovement);

        for (var i = 0; i < input.ChildWidthsIn.Count; i++)
        {
            var suffix = $"-{(char)('A' + i)}";
            var barcode = await documentNumbers.NextRollBarcodeAsync(cancellationToken) + suffix;
            var child = Roll.Create(
                Guid.NewGuid(),
                barcode,
                roll.StockId,
                roll.SupplierLotNumber,
                input.ChildWidthsIn[i],
                remaining,
                now,
                roll.ReceiptId,
                input.Location ?? roll.Location,
                userId,
                now);

            var childMovement = RollMovement.Create(
                Guid.NewGuid(), child.Id, RollMovementType.Split, remaining, null, now,
                $"Split from {roll.RollBarcode}", userId, now);
            child.AddMovement(childMovement);
            db.RollMovements.Add(childMovement);
            db.Rolls.Add(child);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ConsumeAsync(
        Guid id,
        decimal quantityLf,
        Guid? jobId,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var roll = await db.Rolls
            .Include(r => r.Stock)
            .SingleAsync(r => r.Id == id, cancellationToken);

        roll.Consume(quantityLf, userId, now);

        var movement = RollMovement.Create(
            Guid.NewGuid(),
            id,
            RollMovementType.Consume,
            -quantityLf,
            jobId,
            now,
            notes,
            userId,
            now);
        db.RollMovements.Add(movement);
        roll.AddMovement(movement);

        if (jobId is Guid jobGuid)
        {
            var usage = JobMaterialUsage.Create(
                Guid.NewGuid(),
                jobGuid,
                roll.StockId,
                id,
                quantityLf,
                now,
                notes,
                userId,
                now);
            db.JobMaterialUsages.Add(usage);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private Guid RequireUserId() =>
        currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
}
