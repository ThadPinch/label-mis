using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.FinishingOperations;

public record FinishingOperationListItem(Guid Id, string Code, string Description, FinishingOperationType OperationType, bool IsActive);

public record FinishingOperationForm(
    string Code,
    string Description,
    FinishingOperationType OperationType,
    decimal DefaultSetupMinutes,
    decimal DefaultRunSpeedFpm,
    string EquipmentName,
    decimal CostPerHour);

public class FinishingOperationService(LabelsMisDbContext db, ICurrentUserService currentUser)
{
    public async Task<PagedResult<FinishingOperationListItem>> ListAsync(
        string? search, string? sort, int page, int pageSize, bool includeInactive, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);
        var query = db.FinishingOperations.AsNoTracking().AsQueryable();
        if (!includeInactive) query = query.Where(o => o.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(o => o.Code.Contains(term) || o.Description.ToUpper().Contains(term));
        }
        var (sortKey, desc) = QueryExtensions.ParseSort(sort);
        query = sortKey switch
        {
            "code" => query.OrderByDir(desc, o => o.Code),
            "description" => query.OrderByDir(desc, o => o.Description),
            "type" => query.OrderByDir(desc, o => o.OperationType),
            "active" => query.OrderByDir(desc, o => o.IsActive),
            _ => query.OrderBy(o => o.Code)
        };
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new FinishingOperationListItem(o.Id, o.Code, o.Description, o.OperationType, o.IsActive))
            .ToListAsync(ct);
        return new PagedResult<FinishingOperationListItem>(items, page, pageSize, total);
    }

    public Task<FinishingOperation?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.FinishingOperations.FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<FinishingOperation> CreateAsync(FinishingOperationForm form, CancellationToken ct = default)
    {
        var userId = RequireUserId();
        var operation = FinishingOperation.Create(
            Guid.NewGuid(),
            form.Code,
            form.Description,
            form.OperationType,
            form.DefaultSetupMinutes,
            form.DefaultRunSpeedFpm,
            form.EquipmentName,
            form.CostPerHour,
            userId,
            DateTime.UtcNow);
        db.FinishingOperations.Add(operation);
        await db.SaveChangesAsync(ct);
        return operation;
    }

    public async Task UpdateAsync(Guid id, FinishingOperationForm form, CancellationToken ct = default)
    {
        var operation = await db.FinishingOperations.FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new InvalidOperationException("Finishing operation not found.");
        operation.Update(
            form.Code,
            form.Description,
            form.OperationType,
            form.DefaultSetupMinutes,
            form.DefaultRunSpeedFpm,
            form.EquipmentName,
            form.CostPerHour,
            RequireUserId(),
            DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        var operation = await db.FinishingOperations.FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new InvalidOperationException("Finishing operation not found.");
        operation.Deactivate(RequireUserId(), DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    private Guid RequireUserId() =>
        currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
}
