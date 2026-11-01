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

        method.Update("Courier", ShippingMethodType.Delivery, 25m, true, "   ", UserId, DateTime.UtcNow);

        method.Notes.Should().BeNull();
    }

    [Fact]
    public void Create_NotesOverMaxLength_Throws()
    {
        var act = () => ShippingMethod.Create(Guid.NewGuid(), "Courier", ShippingMethodType.Delivery, 25m, true, UserId,
            DateTime.UtcNow, new string('x', ShippingMethod.NotesMaxLength + 1));

        act.Should().Throw<ArgumentException>();
    }
}
