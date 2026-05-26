using LabelsMis.Domain.Entities;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Suppliers;

public record SupplierListItem(Guid Id, string Name, string Code, bool IsActive);

public record SupplierContactInput(
    Guid? Id,
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? Role,
    bool IsPrimary);

public record SupplierForm(
    string Name,
    string Code,
    string Terms,
    int DefaultLeadTimeDays,
    string? AccountNumber,
    IReadOnlyList<SupplierContactInput> Contacts);

public class SupplierService(LabelsMisDbContext db, ICurrentUserService currentUser)
{
    public async Task<PagedResult<SupplierListItem>> ListAsync(
        string? search, string? sort, int page, int pageSize, bool includeInactive, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);
        var query = db.Suppliers.AsNoTracking().AsQueryable();
        if (!includeInactive) query = query.Where(s => s.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(s => s.Name.ToUpper().Contains(term) || s.Code.Contains(term));
        }
        query = sort == "code" ? query.OrderBy(s => s.Code) : query.OrderBy(s => s.Name);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new SupplierListItem(s.Id, s.Name, s.Code, s.IsActive)).ToListAsync(ct);
        return new PagedResult<SupplierListItem>(items, page, pageSize, total);
    }

    public Task<Supplier?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Suppliers.Include(s => s.Contacts).FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<Supplier> CreateAsync(SupplierForm form, CancellationToken ct = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var supplier = Supplier.Create(Guid.NewGuid(), form.Name, form.Code, form.Terms,
            form.DefaultLeadTimeDays, form.AccountNumber, userId, now);
        foreach (var c in form.Contacts)
        {
            var contact = SupplierContact.Create(c.Id ?? Guid.NewGuid(), supplier.Id, c.FirstName, c.LastName,
                c.Email, c.Phone, c.Role, c.IsPrimary, userId, now);
            supplier.AddContact(contact);
        }
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync(ct);
        return supplier;
    }

    public async Task UpdateAsync(Guid id, SupplierForm form, CancellationToken ct = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var supplier = await GetByIdAsync(id, ct) ?? throw new InvalidOperationException("Supplier not found.");
        supplier.Update(form.Name, form.Code, form.Terms, form.DefaultLeadTimeDays, form.AccountNumber, userId, now);
        SyncContacts(supplier, form.Contacts, userId, now);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new InvalidOperationException("Supplier not found.");
        supplier.Deactivate(RequireUserId(), DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    private void SyncContacts(Supplier supplier, IReadOnlyList<SupplierContactInput> inputs, Guid userId, DateTime now)
    {
        var existing = db.SupplierContacts.Where(c => c.SupplierId == supplier.Id).ToList();
        var inputIds = inputs.Where(i => i.Id.HasValue).Select(i => i.Id!.Value).ToHashSet();
        foreach (var removed in existing.Where(c => !inputIds.Contains(c.Id))) db.SupplierContacts.Remove(removed);
        foreach (var input in inputs)
        {
            if (input.Id is Guid contactId)
            {
                existing.FirstOrDefault(c => c.Id == contactId)?.Update(
                    input.FirstName, input.LastName, input.Email, input.Phone, input.Role, input.IsPrimary, userId, now);
            }
            else
            {
                var contact = SupplierContact.Create(Guid.NewGuid(), supplier.Id, input.FirstName, input.LastName,
                    input.Email, input.Phone, input.Role, input.IsPrimary, userId, now);
                supplier.AddContact(contact);
                db.SupplierContacts.Add(contact);
            }
        }
    }

    private Guid RequireUserId() =>
        currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
}
