using LabelsMis.Domain.Carriers;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Tests.Entities;

public class ShippingMethodTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public void CreateDefaults_SeedsCustomerAccountAndFedexNotes()
    {
        var defaults = ShippingMethod.CreateDefaults(UserId, DateTime.UtcNow).ToList();

        defaults.Select(m => m.Id).Should().Equal(
            ShippingMethod.PickupId, ShippingMethod.DeliveryId, ShippingMethod.FedexId, ShippingMethod.CustomerAccountId);
        defaults.Single(m => m.Id == ShippingMethod.CustomerAccountId).Should().Match<ShippingMethod>(m =>
            m.Name == "Customer account"
            && m.MethodType == ShippingMethodType.CustomerAccount
            && m.Price == 0m
            && m.Notes == "Knight Transportation — bills to customer, $0");
        defaults.Single(m => m.Id == ShippingMethod.FedexId).Notes
            .Should().Be("estimate — replaced by actual × markup at shipment");
    }

    [Fact]
    public void Update_BlankNotes_StoredAsNull()
    {
        var method = ShippingMethod.Create(Guid.NewGuid(), "Courier", ShippingMethodType.Delivery, 25m, true, UserId,
            DateTime.UtcNow, "  flat fee  ");
        method.Notes.Should().Be("flat fee");

        method.Update("Courier", ShippingMethodType.Delivery, 25m, true, false, "   ", null, UserId, DateTime.UtcNow);

        method.Notes.Should().BeNull();
    }

    [Fact]
    public void CreateDefaults_OnlyCarrierMethodsNeedAShipmentRecord()
    {
        var defaults = ShippingMethod.CreateDefaults(UserId, DateTime.UtcNow).ToList();

        defaults.Where(m => m.RequiresShipmentRecord).Select(m => m.Id).Should().Equal(ShippingMethod.FedexId);
    }

    [Theory]
    [InlineData(ShippingMethodType.Pickup, false)]
    [InlineData(ShippingMethodType.Delivery, false)]
    [InlineData(ShippingMethodType.Carrier, true)]
    [InlineData(ShippingMethodType.CustomerAccount, false)]
    public void Create_WithoutAFlag_DefaultsTheShipmentRecordFromTheType(ShippingMethodType type, bool expected)
    {
        var method = ShippingMethod.Create(Guid.NewGuid(), "M", type, 0m, true, UserId, DateTime.UtcNow);

        method.RequiresShipmentRecord.Should().Be(expected);
    }

    [Fact]
    public void CreateAndUpdate_AnExplicitShipmentRecordFlag_Wins()
    {
        var method = ShippingMethod.Create(Guid.NewGuid(), "UPS", ShippingMethodType.Carrier, 0m, true, UserId,
            DateTime.UtcNow, requiresShipmentRecord: false);
        method.RequiresShipmentRecord.Should().BeFalse();

        method.Update("UPS", ShippingMethodType.Carrier, 0m, true, true, null, null, UserId, DateTime.UtcNow);

        method.RequiresShipmentRecord.Should().BeTrue();
    }

    [Fact]
    public void Create_NotesOverMaxLength_Throws()
    {
        var act = () => ShippingMethod.Create(Guid.NewGuid(), "Courier", ShippingMethodType.Delivery, 25m, true, UserId,
            DateTime.UtcNow, new string('x', ShippingMethod.NotesMaxLength + 1));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Carrier_IsKeptOnACarrierMethod_AndDroppedOnAnyOther()
    {
        var ups = ShippingMethod.Create(Guid.NewGuid(), "UPS Ground", ShippingMethodType.Carrier, 0m, true, UserId,
            DateTime.UtcNow, carrier: Carrier.Ups);
        ups.Carrier.Should().Be(Carrier.Ups);

        ups.Update("FedEx Ground", ShippingMethodType.Carrier, 0m, true, true, null, Carrier.Fedex, UserId, DateTime.UtcNow);
        ups.Carrier.Should().Be(Carrier.Fedex);

        ups.Update("Will call", ShippingMethodType.Pickup, 0m, false, false, null, Carrier.Fedex, UserId, DateTime.UtcNow);
        ups.Carrier.Should().BeNull("a pickup has no carrier");

        ShippingMethod.Create(Guid.NewGuid(), "Courier", ShippingMethodType.Delivery, 25m, true, UserId, DateTime.UtcNow,
            carrier: Carrier.Ups).Carrier.Should().BeNull();
    }

    [Fact]
    public void SeededMethods_TheCarrierOneIsFedEx_TheOthersHaveNone()
    {
        var seeded = ShippingMethod.CreateDefaults(UserId, DateTime.UtcNow).ToList();

        seeded.Single(m => m.Id == ShippingMethod.FedexId).Carrier.Should().Be(Carrier.Fedex);
        seeded.Where(m => m.Id != ShippingMethod.FedexId).Should().OnlyContain(m => m.Carrier == null);
    }

    [Theory]
    // method type, the method's carrier, starts on the label, carrier selected for typed tracking
    [InlineData(ShippingMethodType.Carrier, Carrier.Ups, true, Carrier.Ups)]
    [InlineData(ShippingMethodType.Carrier, null, true, Carrier.Ups)]
    [InlineData(ShippingMethodType.Carrier, Carrier.Fedex, false, Carrier.Fedex)]
    [InlineData(ShippingMethodType.Carrier, Carrier.Usps, false, Carrier.Usps)]
    [InlineData(ShippingMethodType.Carrier, Carrier.Dhl, false, Carrier.Dhl)]
    [InlineData(ShippingMethodType.Carrier, Carrier.Other, false, Carrier.Other)]
    [InlineData(null, null, true, Carrier.Ups)]
    [InlineData(ShippingMethodType.Pickup, null, false, Carrier.Ups)]
    [InlineData(ShippingMethodType.Delivery, null, false, Carrier.Ups)]
    [InlineData(ShippingMethodType.CustomerAccount, null, false, Carrier.Ups)]
    public void RecordShipment_StartsFromTheOrdersShippingMethod(
        ShippingMethodType? type, Carrier? methodCarrier, bool startsOnLabel, Carrier typedCarrier)
    {
        RecordShipmentDefaults.StartsOnLabel(type, methodCarrier).Should().Be(startsOnLabel);
        RecordShipmentDefaults.CarrierFor(methodCarrier).Should().Be(typedCarrier);
    }
}
