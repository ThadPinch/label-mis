namespace LabelsMis.Domain.Enums;

public enum ShippingMethodType
{
    Pickup = 0,
    Delivery = 1,
    Fedex = 2,

    /// <summary>Shipped on the customer's own carrier account (e.g. Knight Transportation): the
    /// carrier bills the customer, so the shop bills $0 shipping.</summary>
    CustomerAccount = 3
}
