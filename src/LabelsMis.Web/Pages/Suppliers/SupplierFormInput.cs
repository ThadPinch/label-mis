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

    /// <summary>Makes outsourced items for us (promo, print, wide format, or whole label runs);
    /// only these suppliers appear in the vendor pickers on estimates and orders.</summary>
    [Display(Name = "Outsource vendor")]
    public bool IsOutsourceVendor { get; set; }

    [StringLength(2000)]
    [Display(Name = "Outsourcing notes")]
    public string? OutsourceNotes { get; set; }

    /// <summary>At least one contact is required — the form always shows a row and keeps the last one.</summary>
    [MinLength(1, ErrorMessage = "Add at least one contact.")]
    public List<SupplierContactFormInput> Contacts { get; set; } = [new()];

    public SupplierForm ToForm() => new(
        Name,
        Code,
        Terms,
        DefaultLeadTimeDays,
        AccountNumber,
        Contacts.Select(c => new SupplierContactInput(c.Id, c.FirstName, c.LastName, c.Email, c.Phone, c.Role, c.IsPrimary)).ToList(),
        IsOutsourceVendor,
        OutsourceNotes);

    public static SupplierFormInput FromEntity(Domain.Entities.Supplier supplier) => new()
    {
        Name = supplier.Name,
        Code = supplier.Code,
        Terms = supplier.Terms,
        DefaultLeadTimeDays = supplier.DefaultLeadTimeDays,
        AccountNumber = supplier.AccountNumber,
        IsOutsourceVendor = supplier.IsOutsourceVendor,
        OutsourceNotes = supplier.OutsourceNotes,
        // Older suppliers may have no contact yet; show an empty row so the required one can be filled in.
        Contacts = supplier.Contacts.Count == 0
            ? [new()]
            : supplier.Contacts.Select(c => new SupplierContactFormInput
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

    [Required(ErrorMessage = "First name is required.")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Last name is required.")]
    public string LastName { get; set; } = string.Empty;

    [EmailAddress]
    public string? Email { get; set; }

    public string? Phone { get; set; }
    public string? Role { get; set; }
    public bool IsPrimary { get; set; } = true;
}
