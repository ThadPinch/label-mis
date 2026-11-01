using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Shipping;

/// <summary>The shop's shipping-charge settings (Settings → General): the flat local-delivery fee
/// and the multiplier applied to a carrier's actual cost.</summary>
public sealed record ShippingBillingSettings(decimal LocalDeliveryFee, decimal CarrierCostMultiplier)
{
    public const decimal DefaultLocalDeliveryFee = 25m;
    public const decimal DefaultCarrierCostMultiplier = 1.5m;

    public static ShippingBillingSettings Default { get; } =
        new(DefaultLocalDeliveryFee, DefaultCarrierCostMultiplier);
}

/// <summary>What a shipment bills the customer for shipping (Sabrina's rules): pickup and the
/// customer's own carrier account bill $0, local delivery bills the flat fee, and a carrier
/// shipment bills the carrier's actual cost × the multiplier — unknown (null) until that cost is
/// entered. An order with no shipping method is treated as a carrier shipment.</summary>
public static class ShippingBillingRules
{
    public static decimal? BilledShipping(
        ShippingMethodType? methodType,
        decimal? carrierCost,
        ShippingBillingSettings settings) =>
        methodType switch
        {
            ShippingMethodType.Pickup or ShippingMethodType.CustomerAccount => 0m,
            ShippingMethodType.Delivery => RoundCents(settings.LocalDeliveryFee),
            _ => carrierCost is { } cost ? RoundCents(cost * settings.CarrierCostMultiplier) : null
        };

    /// <summary>Plain-language rule for a method, for screens that show the billed amount.</summary>
    public static string Describe(ShippingMethodType? methodType, ShippingBillingSettings settings) =>
        methodType switch
        {
            ShippingMethodType.Pickup => "Pickup — no charge",
            ShippingMethodType.CustomerAccount => "Customer's own carrier account — no charge",
            ShippingMethodType.Delivery => $"Local delivery — flat {settings.LocalDeliveryFee:C2}",
            _ => $"Carrier cost × {settings.CarrierCostMultiplier:0.##}"
        };

    private static decimal RoundCents(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}
