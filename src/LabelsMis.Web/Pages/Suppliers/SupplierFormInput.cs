using System.ComponentModel.DataAnnotations;
using LabelsMis.Web.Services.Suppliers;

namespace LabelsMis.Web.Pages.Suppliers;

public class SupplierFormInput
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Code { get; set; } = string.Empty;

    public string Terms { get; set; } = "Net 30";

    [Range(0, 365)]
    [Display(Name = "Default lead time (days)")]
    public int DefaultLeadTimeDays { get; set; } = 7;

    [Display(Name = "Account number")]
    public string? AccountNumber { get; set; }

    public List<SupplierContactFormInput> Contacts { get; set; } = [new()];

    public SupplierForm ToForm() => new(
        Name,
        Code,
        Terms,
        DefaultLeadTimeDays,
        AccountNumber,
        Contacts.Select(c => new SupplierContactInput(c.Id, c.FirstName, c.LastName, c.Email, c.Phone, c.Role, c.IsPrimary)).ToList());

    public static SupplierFormInput FromEntity(Domain.Entities.Supplier supplier) => new()
    {
        Name = supplier.Name,
        Code = supplier.Code,
        Terms = supplier.Terms,
        DefaultLeadTimeDays = supplier.DefaultLeadTimeDays,
        AccountNumber = supplier.AccountNumber,
        Contacts = supplier.Contacts.Select(c => new SupplierContactFormInput
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

public class SupplierContactFormInput
{
    public Guid? Id { get; set; }

    [Required]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    public string LastName { get; set; } = string.Empty;

    [EmailAddress]
    public string? Email { get; set; }

    public string? Phone { get; set; }
    public string? Role { get; set; }
    public bool IsPrimary { get; set; } = true;
}
