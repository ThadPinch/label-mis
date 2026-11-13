using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Entities;

public class Customer : MasterDataEntity
{
    private readonly List<Address> _addresses = [];
    private readonly List<Contact> _contacts = [];

    private Customer()
    {
    }

    public string Name { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public PaymentTerms Terms { get; private set; } = PaymentTerms.Net30;
    public bool TaxExempt { get; private set; }
    public decimal DefaultMarkupPct { get; private set; }
    public CustomerStatus Status { get; private set; }
    public Guid? SalesRepId { get; private set; }

    /// <summary>Standing internal notes for this customer. Seeds the header notes of a new estimate or
    /// sales order when the customer is picked and prints on every job ticket for the customer; never
    /// printed on a customer-facing document.</summary>
    public string? Notes { get; private set; }

    /// <summary>The customer's id in QuickBooks Online (Phase 1 sends it as <c>CustomerRef</c>).</summary>
    public string? QuickBooksCustomerId { get; private set; }

    /// <summary>The linked QuickBooks customer's display name, kept so the link can be read without
    /// asking QuickBooks. Null for an id that was typed in by hand.</summary>
    public string? QuickBooksCustomerName { get; private set; }

    /// <summary>The new-client form (tax / resale set-up in QuickBooks) is back. Until then the
    /// customer's invoices wait. A human gate: QuickBooks applies the tax, LabelShop doesn't.</summary>
    public bool TaxSetupComplete { get; private set; }
    public DateOnly? TaxSetupCompletedAt { get; private set; }

    public IReadOnlyCollection<Address> Addresses => _addresses;
    public IReadOnlyCollection<Contact> Contacts => _contacts;

    public static Customer Create(
        Guid id,
        string name,
        string code,
        PaymentTerms terms,
        bool taxExempt,
        decimal defaultMarkupPct,
        CustomerStatus status,
        Guid? salesRepId,
        string? notes,
        Guid createdById,
        DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Customer name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Customer code is required.", nameof(code));
        }

        if (defaultMarkupPct < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultMarkupPct), "Default markup cannot be negative.");
        }

        var customer = new Customer
        {
            Name = name.Trim(),
            Code = code.Trim().ToUpperInvariant(),
            Terms = terms,
            TaxExempt = taxExempt,
            DefaultMarkupPct = defaultMarkupPct,
            Status = status,
            SalesRepId = salesRepId,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };
        customer.SetCreated(id, createdById, createdAt);
        return customer;
    }

    public void Update(
        string name,
        string code,
        PaymentTerms terms,
        bool taxExempt,
        decimal defaultMarkupPct,
        CustomerStatus status,
        Guid? salesRepId,
        string? notes,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Customer name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Customer code is required.", nameof(code));
        }

        if (defaultMarkupPct < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultMarkupPct), "Default markup cannot be negative.");
        }

        Name = name.Trim();
        Code = code.Trim().ToUpperInvariant();
        Terms = terms;
        TaxExempt = taxExempt;
        DefaultMarkupPct = defaultMarkupPct;
        Status = status;
        SalesRepId = salesRepId;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>Links the customer to a QuickBooks customer picked from QuickBooks' own list (or
    /// just created there), keeping its name beside the id.</summary>
    public void LinkQuickBooksCustomer(string quickBooksCustomerId, string? displayName, Guid modifiedById, DateTime modifiedAt)
    {
        if (string.IsNullOrWhiteSpace(quickBooksCustomerId))
        {
            throw new ArgumentException("Pick the QuickBooks customer to link.", nameof(quickBooksCustomerId));
        }

        var id = quickBooksCustomerId.Trim();
        if (id.Length > 100)
        {
            throw new ArgumentException("The QuickBooks customer id is at most 100 characters.", nameof(quickBooksCustomerId));
        }

        var name = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        QuickBooksCustomerId = id;
        QuickBooksCustomerName = name is { Length: > 500 } ? name[..500] : name;
        SetModified(modifiedById, modifiedAt);
    }

    public void UnlinkQuickBooksCustomer(Guid modifiedById, DateTime modifiedAt)
    {
        QuickBooksCustomerId = null;
        QuickBooksCustomerName = null;
        SetModified(modifiedById, modifiedAt);
    }

    public void AddAddress(Address address) => _addresses.Add(address);
    public void AddContact(Contact contact) => _contacts.Add(contact);

    /// <summary>Links the customer to QuickBooks and records whether the new-client (tax) form is
    /// back. Ticking it without a date stamps today; unticking clears the date.</summary>
    public void SetQuickBooksSetup(
        string? quickBooksCustomerId,
        bool taxSetupComplete,
        DateOnly? taxSetupCompletedAt,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        var id = string.IsNullOrWhiteSpace(quickBooksCustomerId) ? null : quickBooksCustomerId.Trim();
        if (id is { Length: > 100 })
        {
            throw new ArgumentException("The QuickBooks customer id is at most 100 characters.", nameof(quickBooksCustomerId));
        }

        if (!string.Equals(QuickBooksCustomerId, id, StringComparison.Ordinal))
        {
            // A different id typed by hand: the name on file belongs to the old one.
            QuickBooksCustomerName = null;
        }

        QuickBooksCustomerId = id;
        TaxSetupComplete = taxSetupComplete;
        TaxSetupCompletedAt = taxSetupComplete
            ? taxSetupCompletedAt ?? DateOnly.FromDateTime(modifiedAt)
            : null;
        SetModified(modifiedById, modifiedAt);
    }
}
