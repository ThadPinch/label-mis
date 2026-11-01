using System.ComponentModel.DataAnnotations;
using LabelsMis.Domain.Enums;
using LabelsMis.Web.Pages.Shared;
using LabelsMis.Web.Services.Shipping;

namespace LabelsMis.Web.Pages.ShippingMethods;

public class ShippingMethodFormInput
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Method type")]
    public ShippingMethodType MethodType { get; set; } = ShippingMethodType.Delivery;

    [Range(0, 999999)]
    public decimal Price { get; set; }

    [Display(Name = "Requires shipping address")]
    public bool RequiresAddress { get; set; } = true;

    [StringLength(Domain.Entities.ShippingMethod.NotesMaxLength)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    public ShippingMethodForm ToForm() => new(Name, MethodType, Price, RequiresAddress, Notes);

    public static ShippingMethodFormInput FromEntity(Domain.Entities.ShippingMethod method) => new()
    {
        Name = method.Name,
        MethodType = method.MethodType,
        Price = method.Price,
        RequiresAddress = method.RequiresAddress,
        Notes = method.Notes
    };

    public static ShippingMethodFormInput ForDuplicate(Domain.Entities.ShippingMethod method)
    {
        var input = FromEntity(method);
        input.Name = MasterDataDuplicateHelper.DuplicateCode(method.Name);
        return input;
    }
}
