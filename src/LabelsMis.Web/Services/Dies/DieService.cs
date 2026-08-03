using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Dies;

public record DieListItem(
    Guid Id,
    string Description,
    string? CustomerName,
    decimal LabelAcrossIn,
    decimal LabelAroundIn,
    int LabelsAcross,
    int LabelsAround,
    decimal? DieRepeatIn,
    string? Location,
    bool IsActive);

public record DieForm(
    string Description,
    Guid? CustomerId,
    DieType DieType,
    string? Shape,
    decimal LabelAcrossIn,
    decimal LabelAroundIn,
    decimal CornerRadiusIn,
    decimal GutterAcrossIn,
    decimal GutterAroundIn,
    int LabelsAcross,
    int LabelsAround,
    decimal WebWidthIn,
    Guid? SupplierId,
    string? SupplierPartNumber,
    string? Location,
    decimal? DieRepeatIn,
    string? LinerSpec,
    decimal? SetupRating,
    decimal? SpeedRating);

public class DieService(LabelsMisDbContext db, ICurrentUserService currentUser)
{
    public async Task<PagedResult<DieListItem>> ListAsync(
        string? search, string? sort, int page, int pageSize, bool includeInactive, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);
        var query = db.Dies.AsNoTracking().Include(d => d.Customer).AsQueryable();
        if (!includeInactive) query = query.Where(d => d.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(d => d.Description.ToUpper().Contains(term));
        }
        var (sortKey, desc) = QueryExtensions.ParseSort(sort);
        query = sortKey switch
        {
            "description" => query.OrderByDir(desc, d => d.Description),
            "customer" => query.OrderByDir(desc, d => d.Customer != null ? d.Customer.Name : ""),
            "active" => query.OrderByDir(desc, d => d.IsActive),
            _ => query.OrderBy(d => d.Description)
        };
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(d => new DieListItem(
                d.Id,
                d.Description,
                d.Customer != null ? d.Customer.Name : null,
                d.LabelAcrossIn,
                d.LabelAroundIn,
                d.LabelsAcross,
                d.LabelsAround,
                d.DieRepeatIn,
                d.Location,
                d.IsActive))
            .ToListAsync(ct);
        return new PagedResult<DieListItem>(items, page, pageSize, total);
    }

    public Task<Die?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Dies.Include(d => d.Customer).Include(d => d.Supplier).FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<Die> CreateAsync(DieForm form, CancellationToken ct = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var die = Die.Create(Guid.NewGuid(), form.Description, form.CustomerId, form.DieType, form.Shape,
            form.LabelAcrossIn, form.LabelAroundIn, form.CornerRadiusIn, form.GutterAcrossIn, form.GutterAroundIn,
            form.LabelsAcross, form.LabelsAround, form.WebWidthIn, form.SupplierId, form.SupplierPartNumber, form.Location, userId, now);
        die.UpdateSpecs(form.DieRepeatIn, form.LinerSpec, form.SetupRating, form.SpeedRating, userId, now);
        db.Dies.Add(die);
        await db.SaveChangesAsync(ct);
        return die;
    }

    public async Task UpdateAsync(Guid id, DieForm form, CancellationToken ct = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var die = await db.Dies.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new InvalidOperationException("Die not found.");
        die.UpdateDetails(form.Description, form.CustomerId, form.DieType, form.Shape,
            form.SupplierId, form.SupplierPartNumber, form.Location, userId, now);
        die.UpdateImposition(form.LabelAcrossIn, form.LabelAroundIn, form.CornerRadiusIn,
            form.GutterAcrossIn, form.GutterAroundIn, form.LabelsAcross, form.LabelsAround, form.WebWidthIn, userId, now);
        die.UpdateSpecs(form.DieRepeatIn, form.LinerSpec, form.SetupRating, form.SpeedRating, userId, now);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        var die = await db.Dies.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new InvalidOperationException("Die not found.");
        die.Deactivate(RequireUserId(), DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    public Task<List<(Guid Id, string Name)>> GetCustomerOptionsAsync(CancellationToken ct = default) =>
        db.Customers.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new ValueTuple<Guid, string>(c.Id, c.Name)).ToListAsync(ct);

    public Task<List<(Guid Id, string Name)>> GetSupplierOptionsAsync(CancellationToken ct = default) =>
        db.Suppliers.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Name)
            .Select(s => new ValueTuple<Guid, string>(s.Id, s.Name)).ToListAsync(ct);

    private Guid RequireUserId() =>
        currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
}
