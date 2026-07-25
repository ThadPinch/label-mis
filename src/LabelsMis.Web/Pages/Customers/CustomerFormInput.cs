using System.ComponentModel.DataAnnotations;
using LabelsMis.Domain.Enums;
using LabelsMis.Web.Services.Customers;

namespace LabelsMis.Web.Pages.Customers;

public class CustomerFormInput
{
    [Required]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Code")]
    public string Code { get; set; } = string.Empty;

    [Display(Name = "Terms")]
    public PaymentTerms Terms { get; set; } = PaymentTerms.Net30;

    [Display(Name = "Tax exempt")]
    public bool TaxExempt { get; set; }

    [Range(0, 10)]
    [Display(Name = "Default markup %")]
    public decimal DefaultMarkupPct { get; set; } = 0.45m;

    [Display(Name = "Status")]
    public CustomerStatus Status { get; set; } = CustomerStatus.Active;

    public Guid? SalesRepId { get; set; }

    public List<AddressFormInput> Addresses { get; set; } = [];

    public List<ContactFormInput> Contacts { get; set; } = [];

    public void NormalizeCollections()
    {
        Addresses = Addresses.Where(a => !a.IsEmpty()).ToList();
        Contacts = Contacts.Where(c => !c.IsEmpty()).ToList();
    }

    public CustomerForm ToForm() => new(
        Name,
        Code,
        Terms,
        TaxExempt,
        DefaultMarkupPct,
        Status,
        SalesRepId,
        Addresses.Select(a => new AddressInput(a.Id, a.AddressType, a.Street1, a.Street2, a.City, a.State, a.Zip, a.Country, a.IsDefault)).ToList(),
        Contacts.Select(c => new ContactInput(c.Id, c.FirstName, c.LastName, c.Email, c.Phone, c.Role, c.IsPrimary)).ToList());

    public static CustomerFormInput FromEntity(Domain.Entities.Customer customer) => new()
    {
        Name = customer.Name,
        Code = customer.Code,
        Terms = customer.Terms,
        TaxExempt = customer.TaxExempt,
        DefaultMarkupPct = customer.DefaultMarkupPct,
        Status = customer.Status,
        SalesRepId = customer.SalesRepId,
        Addresses = customer.Addresses.Select(a => new AddressFormInput
        {
            Id = a.Id,
            AddressType = a.AddressType,
            Street1 = a.Street1,
            Street2 = a.Street2,
            City = a.City,
            State = a.State,
            Zip = a.Zip,
            Country = a.Country,
            IsDefault = a.IsDefault
        }).ToList(),
        Contacts = customer.Contacts.Select(c => new ContactFormInput
        {
            Id = c.Id,
            FirstName = c.FirstName,
            LastName = c.LastName,
            Email = c.Email,
            Phone = c.Phone,
            Role = c.Role,
            IsPrimary = c.IsPrimary
        }).ToList()
    };
}

public class AddressFormInput
{
    public Guid? Id { get; set; }

    [Display(Name = "Type")]
    public AddressType AddressType { get; set; } = AddressType.Billing;

    public bool IsEmpty() =>
        string.IsNullOrWhiteSpace(Street1)
        && string.IsNullOrWhiteSpace(Street2)
        && string.IsNullOrWhiteSpace(City)
        && string.IsNullOrWhiteSpace(State)
        && string.IsNullOrWhiteSpace(Zip);

    [Required]
    [Display(Name = "Street")]
    public string Street1 { get; set; } = string.Empty;

    [Display(Name = "Street 2")]
    public string? Street2 { get; set; }

    [Required]
    public string City { get; set; } = string.Empty;

    [Required]
    public string State { get; set; } = string.Empty;

    [Required]
    public string Zip { get; set; } = string.Empty;

    public string Country { get; set; } = "US";

    [Display(Name = "Default")]
    public bool IsDefault { get; set; } = true;
}

public class ContactFormInput
{
    public Guid? Id { get; set; }

    public bool IsEmpty() =>
        string.IsNullOrWhiteSpace(FirstName)
        && string.IsNullOrWhiteSpace(LastName)
        && string.IsNullOrWhiteSpace(Email)
        && string.IsNullOrWhiteSpace(Phone)
        && string.IsNullOrWhiteSpace(Role);

    [Required]
    [Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [EmailAddress]
    public string? Email { get; set; }

    public string? Phone { get; set; }
    public string? Role { get; set; }

    [Display(Name = "Primary contact")]
    public bool IsPrimary { get; set; } = true;
}
