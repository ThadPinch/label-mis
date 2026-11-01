using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.ValueObjects;

namespace LabelsMis.Domain.Tests.Entities;

public class ShipmentBillingTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CreatePending_CarrierCost_IsStoredAndDefaultsToUnknown()
    {
        Pending(carrierCost: 20m).CarrierCost.Should().Be(20m);
        Pending().CarrierCost.Should().BeNull();
    }

    [Fact]
    public void SetCarrierCost_EntersCorrectsAndClears()
    {
        var shipment = Pending();

        shipment.SetCarrierCost(20m, UserId, Now);
        shipment.CarrierCost.Should().Be(20m);

        shipment.SetCarrierCost(null, UserId, Now);
        shipment.CarrierCost.Should().BeNull();
    }

    [Fact]
    public void SetCarrierCost_Negative_Throws()
    {
        var act = () => Pending().SetCarrierCost(-1m, UserId, Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    internal static Shipment Pending(decimal? carrierCost = null) => Shipment.CreatePending(
        Guid.NewGuid(), "SHP-2026-00099", Guid.NewGuid(), DateOnly.FromDateTime(Now),
        Carrier.Fedex, null, null, ShippingAddress.Empty, ShippingAddress.Empty, null,
        0m, BillingType.Sender, null, UserId, Now, carrierCost);
}
