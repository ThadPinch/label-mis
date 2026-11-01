using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Shipping;

namespace LabelsMis.Domain.Tests.Shipping;

public class ShippingBillingRulesTests
{
    private static readonly ShippingBillingSettings Settings = ShippingBillingSettings.Default;

    [Theory]
    [InlineData(ShippingMethodType.Pickup, null, 0d)]
    [InlineData(ShippingMethodType.Pickup, 40d, 0d)]
    [InlineData(ShippingMethodType.CustomerAccount, null, 0d)]
    [InlineData(ShippingMethodType.CustomerAccount, 40d, 0d)]
    [InlineData(ShippingMethodType.Delivery, null, 25d)]
    [InlineData(ShippingMethodType.Delivery, 12d, 25d)]
    [InlineData(ShippingMethodType.Fedex, 20d, 30d)]
    public void BilledShipping_FollowsSabrinasRules(ShippingMethodType type, double? carrierCost, double expected)
    {
        ShippingBillingRules.BilledShipping(type, (decimal?)carrierCost, Settings).Should().Be((decimal)expected);
    }

    [Fact]
    public void BilledShipping_CarrierWithoutCost_IsPending()
    {
        ShippingBillingRules.BilledShipping(ShippingMethodType.Fedex, null, Settings).Should().BeNull();
    }

    [Fact]
    public void BilledShipping_NoMethodOnTheOrder_IsTreatedAsACarrierShipment()
    {
        ShippingBillingRules.BilledShipping(null, 20m, Settings).Should().Be(30m);
        ShippingBillingRules.BilledShipping(null, null, Settings).Should().BeNull();
    }

    [Fact]
    public void BilledShipping_RoundsToCents()
    {
        // 13.37 × 1.5 = 20.055 → 20.06 (half away from zero)
        ShippingBillingRules.BilledShipping(ShippingMethodType.Fedex, 13.37m, Settings).Should().Be(20.06m);
    }

    [Fact]
    public void BilledShipping_UsesTheShopsSettings()
    {
        var custom = new ShippingBillingSettings(LocalDeliveryFee: 30m, CarrierCostMultiplier: 1.25m);

        ShippingBillingRules.BilledShipping(ShippingMethodType.Delivery, null, custom).Should().Be(30m);
        ShippingBillingRules.BilledShipping(ShippingMethodType.Fedex, 20m, custom).Should().Be(25m);
    }

    [Fact]
    public void GeneralSettings_DefaultsToTwentyFiveAndOnePointFive()
    {
        var settings = GeneralSettings.CreateDefault(Guid.NewGuid(), DateTime.UtcNow);

        settings.ShippingBilling.Should().Be(new ShippingBillingSettings(25m, 1.5m));
    }

    [Theory]
    [InlineData(-1, 1.5)]
    [InlineData(25, -0.1)]
    [InlineData(25, 10.5)]
    public void GeneralSettings_UpdateShippingBilling_RejectsOutOfRange(double fee, double multiplier)
    {
        var settings = GeneralSettings.CreateDefault(Guid.NewGuid(), DateTime.UtcNow);

        var act = () => settings.UpdateShippingBilling((decimal)fee, (decimal)multiplier, Guid.NewGuid(), DateTime.UtcNow);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
