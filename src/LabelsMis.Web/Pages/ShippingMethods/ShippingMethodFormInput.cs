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

    public ShippingMethodForm ToForm() => new(Name, MethodType, Price, RequiresAddress);

    public static ShippingMethodFormInput FromEntity(Domain.Entities.ShippingMethod method) => new()
    {
        Name = method.Name,
        MethodType = method.MethodType,
        Price = method.Price,
        RequiresAddress = method.RequiresAddress
    };

    public static ShippingMethodFormInput ForDuplicate(Domain.Entities.ShippingMethod method)
    {
        var input = FromEntity(method);
        input.Name = MasterDataDuplicateHelper.DuplicateCode(method.Name);
        return input;
    }
}
