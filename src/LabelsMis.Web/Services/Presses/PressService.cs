using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Presses;

public record PressListItem(Guid Id, string Name, string Code, PressType PressType, bool IsActive);

public record PressForm(
    string Name,
    string Code,
    PressType PressType,
    decimal WebWidthIn,
    decimal MaxRepeatIn,
    decimal MinRepeatIn,
    int MaxColors,
    decimal SpeedFpm,
    decimal SetupMinutes,
    decimal CostPerHour,
    bool IsClickBased);

public class PressService(LabelsMisDbContext db, ICurrentUserService currentUser)
{
    public async Task<PagedResult<PressListItem>> ListAsync(
        string? search, int page, int pageSize, bool includeInactive, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);
        var query = db.Presses.AsNoTracking().AsQueryable();
        if (!includeInactive) query = query.Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(p => p.Name.ToUpper().Contains(term) || p.Code.Contains(term));
        }
        query = query.OrderBy(p => p.Name);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new PressListItem(p.Id, p.Name, p.Code, p.PressType, p.IsActive)).ToListAsync(ct);
        return new PagedResult<PressListItem>(items, page, pageSize, total);
    }

    public Task<Press?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Presses.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Press> CreateAsync(PressForm form, CancellationToken ct = default)
    {
        var userId = RequireUserId();
        var press = Press.Create(Guid.NewGuid(), form.Name, form.Code, form.PressType, form.WebWidthIn,
            form.MaxRepeatIn, form.MinRepeatIn, form.MaxColors, form.SpeedFpm, form.SetupMinutes,
            form.CostPerHour, form.IsClickBased, userId, DateTime.UtcNow);
        db.Presses.Add(press);
        await db.SaveChangesAsync(ct);
        return press;
    }

    public async Task UpdateAsync(Guid id, PressForm form, CancellationToken ct = default)
    {
        var press = await db.Presses.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new InvalidOperationException("Press not found.");
        press.Update(form.Name, form.Code, form.PressType, form.WebWidthIn, form.MaxRepeatIn, form.MinRepeatIn,
            form.MaxColors, form.SpeedFpm, form.SetupMinutes, form.CostPerHour, form.IsClickBased,
            RequireUserId(), DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        var press = await db.Presses.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new InvalidOperationException("Press not found.");
        press.Deactivate(RequireUserId(), DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    private Guid RequireUserId() =>
        currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
}
