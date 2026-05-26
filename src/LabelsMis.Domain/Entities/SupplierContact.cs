using LabelsMis.Domain.Common;

namespace LabelsMis.Domain.Entities;

public class SupplierContact : EntityBase
{
    private SupplierContact()
    {
    }

    public Guid SupplierId { get; private set; }
    public Supplier Supplier { get; private set; } = null!;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? Role { get; private set; }
    public bool IsPrimary { get; private set; }

    public string FullName => $"{FirstName} {LastName}".Trim();

    public static SupplierContact Create(
        Guid id,
        Guid supplierId,
        string firstName,
        string lastName,
        string? email,
        string? phone,
        string? role,
        bool isPrimary,
        Guid createdById,
        DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(firstName))
        {
            throw new ArgumentException("First name is required.", nameof(firstName));
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            throw new ArgumentException("Last name is required.", nameof(lastName));
        }

        var contact = new SupplierContact
        {
            SupplierId = supplierId,
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            Role = string.IsNullOrWhiteSpace(role) ? null : role.Trim(),
            IsPrimary = isPrimary
        };
        contact.SetCreated(id, createdById, createdAt);
        return contact;
    }

    public void Update(
        string firstName,
        string lastName,
        string? email,
        string? phone,
        string? role,
        bool isPrimary,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        if (string.IsNullOrWhiteSpace(firstName))
        {
            throw new ArgumentException("First name is required.", nameof(firstName));
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            throw new ArgumentException("Last name is required.", nameof(lastName));
        }

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        Role = string.IsNullOrWhiteSpace(role) ? null : role.Trim();
        IsPrimary = isPrimary;
        SetModified(modifiedById, modifiedAt);
    }
}
