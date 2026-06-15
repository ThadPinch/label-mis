using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Inks;

public record InkListItem(Guid Id, string Code, string Description, InkSet InkSet, bool IsActive);

public record InkForm(
    string Code,
    string Description,
    InkSet InkSet,
    decimal ClickRatePer1000,
    bool IsWhite,
    bool IsSilver,
    decimal BottleCost,
    decimal BottleSizeMl,
    decimal MlPer1000SqIn,
    decimal DefaultCoveragePct);

public class InkService(LabelsMisDbContext db, ICurrentUserService currentUser)
{
    public async Task<PagedResult<InkListItem>> ListAsync(
        string? search, int page, int pageSize, bool includeInactive, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);
        var query = db.Inks.AsNoTracking().AsQueryable();
        if (!includeInactive) query = query.Where(i => i.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(i => i.Code.Contains(term) || i.Description.ToUpper().Contains(term));
        }
        query = query.OrderBy(i => i.Code);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(i => new InkListItem(i.Id, i.Code, i.Description, i.InkSet, i.IsActive)).ToListAsync(ct);
        return new PagedResult<InkListItem>(items, page, pageSize, total);
    }

    public Task<Ink?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Inks.FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<Ink> CreateAsync(InkForm form, CancellationToken ct = default)
    {
        var userId = RequireUserId();
        var ink = Ink.Create(Guid.NewGuid(), form.Code, form.Description, form.InkSet,
            form.ClickRatePer1000, form.IsWhite, form.IsSilver, form.BottleCost, form.BottleSizeMl,
            form.MlPer1000SqIn, form.DefaultCoveragePct, userId, DateTime.UtcNow);
        db.Inks.Add(ink);
        await db.SaveChangesAsync(ct);
        return ink;
    }

    public async Task UpdateAsync(Guid id, InkForm form, CancellationToken ct = default)
    {
        var ink = await db.Inks.FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new InvalidOperationException("Ink not found.");
        ink.Update(form.Code, form.Description, form.InkSet, form.ClickRatePer1000, form.IsWhite,
            form.IsSilver, form.BottleCost, form.BottleSizeMl, form.MlPer1000SqIn, form.DefaultCoveragePct,
            RequireUserId(), DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        var ink = await db.Inks.FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new InvalidOperationException("Ink not found.");
        ink.Deactivate(RequireUserId(), DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    private Guid RequireUserId() =>
        currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
}
