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
    public string Terms { get; private set; } = string.Empty;
    public bool TaxExempt { get; private set; }
    public decimal DefaultMarkupPct { get; private set; }
    public CustomerStatus Status { get; private set; }
    public Guid? SalesRepId { get; private set; }

    public IReadOnlyCollection<Address> Addresses => _addresses;
    public IReadOnlyCollection<Contact> Contacts => _contacts;

    public static Customer Create(
        Guid id,
        string name,
        string code,
        string terms,
        bool taxExempt,
        decimal defaultMarkupPct,
        CustomerStatus status,
        Guid? salesRepId,
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
            Terms = terms.Trim(),
            TaxExempt = taxExempt,
            DefaultMarkupPct = defaultMarkupPct,
            Status = status,
            SalesRepId = salesRepId
        };
        customer.SetCreated(id, createdById, createdAt);
        return customer;
    }

    public void Update(
        string name,
        string code,
        string terms,
        bool taxExempt,
        decimal defaultMarkupPct,
        CustomerStatus status,
        Guid? salesRepId,
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
        Terms = terms.Trim();
        TaxExempt = taxExempt;
        DefaultMarkupPct = defaultMarkupPct;
        Status = status;
        SalesRepId = salesRepId;
        SetModified(modifiedById, modifiedAt);
    }

    public void AddAddress(Address address) => _addresses.Add(address);
    public void AddContact(Contact contact) => _contacts.Add(contact);
}
