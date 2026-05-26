using LabelsMis.Domain.Entities;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Stocks;

public record StockListItem(Guid Id, string Code, string Description, string SupplierName, bool IsActive);

public record StockForm(
    string Code,
    string Description,
    string FaceMaterial,
    string Adhesive,
    string Liner,
    decimal TotalCaliperMil,
    decimal WidthIn,
    Guid SupplierId,
    string? SupplierPartNumber,
    decimal CostPerMsi,
    decimal MinOrderQtyLf);

public class StockService(LabelsMisDbContext db, ICurrentUserService currentUser)
{
    public async Task<PagedResult<StockListItem>> ListAsync(
        string? search, string? sort, int page, int pageSize, bool includeInactive, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);
        var query = db.Stocks.AsNoTracking().Include(s => s.Supplier).AsQueryable();
        if (!includeInactive) query = query.Where(s => s.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(s => s.Code.Contains(term) || s.Description.ToUpper().Contains(term));
        }
        query = sort == "code" ? query.OrderBy(s => s.Code) : query.OrderBy(s => s.Description);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new StockListItem(s.Id, s.Code, s.Description, s.Supplier.Name, s.IsActive)).ToListAsync(ct);
        return new PagedResult<StockListItem>(items, page, pageSize, total);
    }

    public Task<Stock?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Stocks.Include(s => s.Supplier).Include(s => s.CostHistory).FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<Stock> CreateAsync(StockForm form, CancellationToken ct = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var stock = Stock.Create(Guid.NewGuid(), form.Code, form.Description, form.FaceMaterial, form.Adhesive,
            form.Liner, form.TotalCaliperMil, form.WidthIn, form.SupplierId, form.SupplierPartNumber,
            form.CostPerMsi, form.MinOrderQtyLf, userId, now);
        stock.RecordCostChange(Guid.NewGuid(), form.CostPerMsi, now.Date, userId, now);
        db.Stocks.Add(stock);
        await db.SaveChangesAsync(ct);
        return stock;
    }

    public async Task UpdateAsync(Guid id, StockForm form, CancellationToken ct = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var stock = await db.Stocks
            .Include(s => s.CostHistory)        // ← add this
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new InvalidOperationException("Stock not found.");

        if (stock.CostPerMsi != form.CostPerMsi)
        {
            stock.RecordCostChange(Guid.NewGuid(), form.CostPerMsi, now.Date, userId, now);
        }

        stock.Update(form.Code, form.Description, form.FaceMaterial, form.Adhesive, form.Liner,
            form.TotalCaliperMil, form.WidthIn, form.SupplierId, form.SupplierPartNumber,
            form.CostPerMsi, form.MinOrderQtyLf, userId, now);

        await db.SaveChangesAsync(ct);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        var stock = await db.Stocks.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new InvalidOperationException("Stock not found.");
        stock.Deactivate(RequireUserId(), DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    public Task<List<(Guid Id, string Name)>> GetSupplierOptionsAsync(CancellationToken ct = default) =>
        db.Suppliers.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Name)
            .Select(s => new ValueTuple<Guid, string>(s.Id, s.Name)).ToListAsync(ct);

    private Guid RequireUserId() =>
        currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
}
