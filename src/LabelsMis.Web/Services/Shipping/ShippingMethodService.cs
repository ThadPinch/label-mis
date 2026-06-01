using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Shipping;

public record ShippingMethodListItem(
    Guid Id, string Name, ShippingMethodType MethodType, decimal Price, bool RequiresAddress, bool IsActive);

public record ShippingMethodForm(string Name, ShippingMethodType MethodType, decimal Price, bool RequiresAddress);

public class ShippingMethodService(LabelsMisDbContext db, ICurrentUserService currentUser)
{
    public async Task<PagedResult<ShippingMethodListItem>> ListAsync(
        string? search, int page, int pageSize, bool includeInactive, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);
        var query = db.ShippingMethods.AsNoTracking().AsQueryable();
        if (!includeInactive) query = query.Where(m => m.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(m => m.Name.ToUpper().Contains(term));
        }
        query = query.OrderBy(m => m.MethodType).ThenBy(m => m.Name);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(m => new ShippingMethodListItem(m.Id, m.Name, m.MethodType, m.Price, m.RequiresAddress, m.IsActive))
            .ToListAsync(ct);
        return new PagedResult<ShippingMethodListItem>(items, page, pageSize, total);
    }

    public Task<ShippingMethod?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.ShippingMethods.FirstOrDefaultAsync(m => m.Id == id, ct);

    /// <summary>Active methods (plus the supplied one, even if inactive) for selection on transactions.</summary>
    public async Task<IReadOnlyList<ShippingMethodListItem>> GetSelectableAsync(
        Guid? includeId = null, CancellationToken ct = default) =>
        await db.ShippingMethods.AsNoTracking()
            .Where(m => m.IsActive || (includeId != null && m.Id == includeId))
            .OrderBy(m => m.MethodType).ThenBy(m => m.Name)
            .Select(m => new ShippingMethodListItem(m.Id, m.Name, m.MethodType, m.Price, m.RequiresAddress, m.IsActive))
            .ToListAsync(ct);

    public async Task<ShippingMethod> CreateAsync(ShippingMethodForm form, CancellationToken ct = default)
    {
        var userId = RequireUserId();
        var method = ShippingMethod.Create(
            Guid.NewGuid(),
            form.Name,
            form.MethodType,
            form.Price,
            form.RequiresAddress,
            userId,
            DateTime.UtcNow);
        db.ShippingMethods.Add(method);
        await db.SaveChangesAsync(ct);
        return method;
    }

    public async Task UpdateAsync(Guid id, ShippingMethodForm form, CancellationToken ct = default)
    {
        var method = await db.ShippingMethods.FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new InvalidOperationException("Shipping method not found.");
        method.Update(form.Name, form.MethodType, form.Price, form.RequiresAddress, RequireUserId(), DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        var method = await db.ShippingMethods.FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new InvalidOperationException("Shipping method not found.");
        method.Deactivate(RequireUserId(), DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    private Guid RequireUserId() =>
        currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
}
