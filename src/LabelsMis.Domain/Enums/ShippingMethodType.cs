namespace LabelsMis.Domain.Enums;

public enum ShippingMethodType
{
    Pickup = 0,
    Delivery = 1,
    /// <summary>Shipped by a parcel carrier on the shop's account (UPS through Worldwide Express):
    /// billed at the carrier's cost × the multiplier. Stored as 2, which was named Fedex before the
    /// shop's carrier was known.</summary>
    Carrier = 2,

    /// <summary>Shipped on the customer's own carrier account (e.g. Knight Transportation): the
    /// carrier bills the customer, so the shop bills $0 shipping.</summary>
    CustomerAccount = 3
}
