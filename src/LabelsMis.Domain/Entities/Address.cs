using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Entities;

public class Address : EntityBase
{
    private Address()
    {
    }

    public Guid CustomerId { get; private set; }
    public Customer Customer { get; private set; } = null!;
    public AddressType AddressType { get; private set; }
    public string Street1 { get; private set; } = string.Empty;
    public string? Street2 { get; private set; }
    public string City { get; private set; } = string.Empty;
    public string State { get; private set; } = string.Empty;
    public string Zip { get; private set; } = string.Empty;
    public string Country { get; private set; } = "US";
    public bool IsDefault { get; private set; }

    public static Address Create(
        Guid id,
        Guid customerId,
        AddressType addressType,
        string street1,
        string? street2,
        string city,
        string state,
        string zip,
        string country,
        bool isDefault,
        Guid createdById,
        DateTime createdAt)
    {
        ValidateRequired(street1, city, state, zip);

        var address = new Address
        {
            CustomerId = customerId,
            AddressType = addressType,
            Street1 = street1.Trim(),
            Street2 = string.IsNullOrWhiteSpace(street2) ? null : street2.Trim(),
            City = city.Trim(),
            State = state.Trim(),
            Zip = zip.Trim(),
            Country = string.IsNullOrWhiteSpace(country) ? "US" : country.Trim(),
            IsDefault = isDefault
        };
        address.SetCreated(id, createdById, createdAt);
        return address;
    }

    public void Update(
        AddressType addressType,
        string street1,
        string? street2,
        string city,
        string state,
        string zip,
        string country,
        bool isDefault,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        ValidateRequired(street1, city, state, zip);

        AddressType = addressType;
        Street1 = street1.Trim();
        Street2 = string.IsNullOrWhiteSpace(street2) ? null : street2.Trim();
        City = city.Trim();
        State = state.Trim();
        Zip = zip.Trim();
        Country = string.IsNullOrWhiteSpace(country) ? "US" : country.Trim();
        IsDefault = isDefault;
        SetModified(modifiedById, modifiedAt);
    }

    private static void ValidateRequired(string street1, string city, string state, string zip)
    {
        if (string.IsNullOrWhiteSpace(street1))
        {
            throw new ArgumentException("Street address is required.", nameof(street1));
        }

        if (string.IsNullOrWhiteSpace(city))
        {
            throw new ArgumentException("City is required.", nameof(city));
        }

        if (string.IsNullOrWhiteSpace(state))
        {
            throw new ArgumentException("State is required.", nameof(state));
        }

        if (string.IsNullOrWhiteSpace(zip))
        {
            throw new ArgumentException("Zip is required.", nameof(zip));
        }
    }
}
