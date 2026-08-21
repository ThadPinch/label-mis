using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Customers;

public record CustomerListItem(
    Guid Id,
    string Name,
    string Code,
    CustomerStatus Status,
    bool IsActive);

public record AddressInput(
    Guid? Id,
    AddressType AddressType,
    string Street1,
    string? Street2,
    string City,
    string State,
    string Zip,
    string Country,
    bool IsDefault);

public record CustomerAddressOption(
    Guid Id,
    AddressType AddressType,
    string Street1,
    string? Street2,
    string City,
    string State,
    string Zip,
    string Country,
    bool IsDefault);

public record ContactInput(
    Guid? Id,
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? Role,
    bool IsPrimary);

public record CustomerForm(
    string Name,
    string Code,
    PaymentTerms Terms,
    bool TaxExempt,
    decimal DefaultMarkupPct,
    CustomerStatus Status,
    Guid? SalesRepId,
    string? Notes,
    IReadOnlyList<AddressInput> Addresses,
    IReadOnlyList<ContactInput> Contacts);

public class CustomerService(LabelsMisDbContext db, ICurrentUserService currentUser)
{
    public async Task<PagedResult<CustomerListItem>> ListAsync(
        string? search,
        string? sort,
        int page,
        int pageSize,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);

        var query = db.Customers.AsNoTracking().AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(c => c.Name.ToUpper().Contains(term) || c.Code.Contains(term));
        }

        var (sortKey, desc) = QueryExtensions.ParseSort(sort);
        query = sortKey switch
        {
            "name" => query.OrderByDir(desc, c => c.Name),
            "code" => query.OrderByDir(desc, c => c.Code),
            "status" => query.OrderByDir(desc, c => c.Status).ThenBy(c => c.Name),
            "active" => query.OrderByDir(desc, c => c.IsActive),
            _ => query.OrderBy(c => c.Name)
        };

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CustomerListItem(c.Id, c.Name, c.Code, c.Status, c.IsActive))
            .ToListAsync(cancellationToken);

        return new PagedResult<CustomerListItem>(items, page, pageSize, total);
    }

    public Task<Customer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Customers
            .Include(c => c.Addresses)
            .Include(c => c.Contacts)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    /// <summary>The customer's standing notes, for pre-filling a new estimate/order's header notes.</summary>
    public Task<string?> GetNotesAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId)
            .Select(c => c.Notes)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Addresses for a customer, default-first within each type, for ship-to pickers.</summary>
    public async Task<IReadOnlyList<CustomerAddressOption>> GetAddressOptionsAsync(
        Guid customerId, CancellationToken cancellationToken = default) =>
        await db.Addresses.AsNoTracking()
            .Where(a => a.CustomerId == customerId)
            .OrderBy(a => a.AddressType)
            .ThenByDescending(a => a.IsDefault)
            .Select(a => new CustomerAddressOption(
                a.Id,
                a.AddressType,
                a.Street1,
                a.Street2,
                a.City,
                a.State,
                a.Zip,
                a.Country,
                a.IsDefault))
            .ToListAsync(cancellationToken);

    public async Task<Customer> CreateAsync(CustomerForm form, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var customer = Customer.Create(
            Guid.NewGuid(),
            form.Name,
            form.Code,
            form.Terms,
            form.TaxExempt,
            form.DefaultMarkupPct,
            form.Status,
            form.SalesRepId,
            form.Notes,
            userId,
            now);

        foreach (var address in form.Addresses)
        {
            customer.AddAddress(Address.Create(
                address.Id ?? Guid.NewGuid(),
                customer.Id,
                address.AddressType,
                address.Street1,
                address.Street2,
                address.City,
                address.State,
                address.Zip,
                address.Country,
                address.IsDefault,
                userId,
                now));
        }

        foreach (var contact in form.Contacts)
        {
            customer.AddContact(Contact.Create(
                contact.Id ?? Guid.NewGuid(),
                customer.Id,
                contact.FirstName,
                contact.LastName,
                contact.Email,
                contact.Phone,
                contact.Role,
                contact.IsPrimary,
                userId,
                now));
        }

        db.Customers.Add(customer);
        await db.SaveChangesAsync(cancellationToken);
        return customer;
    }

    public async Task UpdateAsync(Guid id, CustomerForm form, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var now = DateTime.UtcNow;
        var customer = await GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Customer not found.");

        customer.Update(
            form.Name,
            form.Code,
            form.Terms,
            form.TaxExempt,
            form.DefaultMarkupPct,
            form.Status,
            form.SalesRepId,
            form.Notes,
            userId,
            now);

        SyncAddresses(customer, form.Addresses, userId, now);
        SyncContacts(customer, form.Contacts, userId, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Customer not found.");
        customer.Deactivate(userId, DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    private void SyncAddresses(Customer customer, IReadOnlyList<AddressInput> inputs, Guid userId, DateTime now)
    {
        var existing = db.Addresses.Where(a => a.CustomerId == customer.Id).ToList();
        var inputIds = inputs.Where(i => i.Id.HasValue).Select(i => i.Id!.Value).ToHashSet();

        foreach (var removed in existing.Where(a => !inputIds.Contains(a.Id)))
        {
            db.Addresses.Remove(removed);
        }

        foreach (var input in inputs)
        {
            if (input.Id is Guid addressId)
            {
                var address = existing.FirstOrDefault(a => a.Id == addressId);
                address?.Update(
                    input.AddressType,
                    input.Street1,
                    input.Street2,
                    input.City,
                    input.State,
                    input.Zip,
                    input.Country,
                    input.IsDefault,
                    userId,
                    now);
            }
            else
            {
                var address = Address.Create(
                    Guid.NewGuid(),
                    customer.Id,
                    input.AddressType,
                    input.Street1,
                    input.Street2,
                    input.City,
                    input.State,
                    input.Zip,
                    input.Country,
                    input.IsDefault,
                    userId,
                    now);
                customer.AddAddress(address);
                db.Addresses.Add(address);
            }
        }
    }

    private void SyncContacts(Customer customer, IReadOnlyList<ContactInput> inputs, Guid userId, DateTime now)
    {
        var existing = db.Contacts.Where(c => c.CustomerId == customer.Id).ToList();
        var inputIds = inputs.Where(i => i.Id.HasValue).Select(i => i.Id!.Value).ToHashSet();

        foreach (var removed in existing.Where(c => !inputIds.Contains(c.Id)))
        {
            db.Contacts.Remove(removed);
        }

        foreach (var input in inputs)
        {
            if (input.Id is Guid contactId)
            {
                var contact = existing.FirstOrDefault(c => c.Id == contactId);
                contact?.Update(
                    input.FirstName,
                    input.LastName,
                    input.Email,
                    input.Phone,
                    input.Role,
                    input.IsPrimary,
                    userId,
                    now);
            }
            else
            {
                var contact = Contact.Create(
                    Guid.NewGuid(),
                    customer.Id,
                    input.FirstName,
                    input.LastName,
                    input.Email,
                    input.Phone,
                    input.Role,
                    input.IsPrimary,
                    userId,
                    now);
                customer.AddContact(contact);
                db.Contacts.Add(contact);
            }
        }
    }

    private Guid RequireUserId() =>
        currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
}
