using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Shipping;
using LabelsMis.Domain.ValueObjects;

namespace LabelsMis.Domain.Tests.Entities;

public class ShipmentBillingTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ShippingBillingSettings Settings = ShippingBillingSettings.Default;

    [Fact]
    public void CreatePending_CarrierCost_IsStoredAndDefaultsToUnknown()
    {
        Pending(carrierCost: 20m).CarrierCost.Should().Be(20m);
        Pending().CarrierCost.Should().BeNull();
        Pending().BilledShipping.Should().BeNull("billed shipping is derived once the method is applied");
    }

    [Fact]
    public void SetCarrierCost_OnFedexShipment_Bills20As30()
    {
        var shipment = Pending();

        shipment.SetCarrierCost(20m, ShippingMethodType.Fedex, Settings, UserId, Now);

        shipment.CarrierCost.Should().Be(20m);
        shipment.BilledShipping.Should().Be(30m);
    }

    [Fact]
    public void SetCarrierCost_Cleared_BilledShippingBackToPending()
    {
        var shipment = Pending();
        shipment.SetCarrierCost(20m, ShippingMethodType.Fedex, Settings, UserId, Now);

        shipment.SetCarrierCost(null, ShippingMethodType.Fedex, Settings, UserId, Now);

        shipment.CarrierCost.Should().BeNull();
        shipment.BilledShipping.Should().BeNull();
    }

    [Fact]
    public void ComputeBilledShipping_PickupIgnoresAnyCarrierCost()
    {
        var shipment = Pending(carrierCost: 18m);

        shipment.ComputeBilledShipping(ShippingMethodType.Pickup, Settings);

        shipment.BilledShipping.Should().Be(0m);
    }

    [Fact]
    public void SetCarrierCost_Negative_Throws()
    {
        var act = () => Pending().SetCarrierCost(-1m, ShippingMethodType.Fedex, Settings, UserId, Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    internal static Shipment Pending(decimal? carrierCost = null) => Shipment.CreatePending(
        Guid.NewGuid(), "SHP-2026-00099", Guid.NewGuid(), DateOnly.FromDateTime(Now),
        Carrier.Fedex, null, null, ShippingAddress.Empty, ShippingAddress.Empty, null,
        0m, BillingType.Sender, null, UserId, Now, carrierCost);
}
