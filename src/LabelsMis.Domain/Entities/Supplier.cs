using LabelsMis.Domain.Common;

namespace LabelsMis.Domain.Entities;

public class Supplier : MasterDataEntity
{
    private readonly List<SupplierContact> _contacts = [];

    private Supplier()
    {
    }

    public string Name { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string Terms { get; private set; } = string.Empty;
    public int DefaultLeadTimeDays { get; private set; }
    public string? AccountNumber { get; private set; }

    public IReadOnlyCollection<SupplierContact> Contacts => _contacts;

    public static Supplier Create(
        Guid id,
        string name,
        string code,
        string terms,
        int defaultLeadTimeDays,
        string? accountNumber,
        Guid createdById,
        DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Supplier name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Supplier code is required.", nameof(code));
        }

        if (defaultLeadTimeDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultLeadTimeDays), "Lead time cannot be negative.");
        }

        var supplier = new Supplier
        {
            Name = name.Trim(),
            Code = code.Trim().ToUpperInvariant(),
            Terms = terms.Trim(),
            DefaultLeadTimeDays = defaultLeadTimeDays,
            AccountNumber = string.IsNullOrWhiteSpace(accountNumber) ? null : accountNumber.Trim()
        };
        supplier.SetCreated(id, createdById, createdAt);
        return supplier;
    }

    public void Update(
        string name,
        string code,
        string terms,
        int defaultLeadTimeDays,
        string? accountNumber,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Supplier name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Supplier code is required.", nameof(code));
        }

        if (defaultLeadTimeDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultLeadTimeDays), "Lead time cannot be negative.");
        }

        Name = name.Trim();
        Code = code.Trim().ToUpperInvariant();
        Terms = terms.Trim();
        DefaultLeadTimeDays = defaultLeadTimeDays;
        AccountNumber = string.IsNullOrWhiteSpace(accountNumber) ? null : accountNumber.Trim();
        SetModified(modifiedById, modifiedAt);
    }

    public void AddContact(SupplierContact contact) => _contacts.Add(contact);
}
