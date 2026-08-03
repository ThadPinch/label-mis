using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.ValueObjects;

namespace LabelsMis.Domain.Tests.Entities;

public class SalesOrderTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public void UpdateOpen_WhenInProduction_Throws()
    {
        var order = SalesOrder.CreateOpen(Guid.NewGuid(), "SO-2026-00001", Guid.NewGuid(),
            null, null, null, Now, null, null, null, null, 0m, ShippingAddress.Empty, UserId, Now);
        order.AdvanceStatus(SalesOrderStatus.InProduction, UserId, Now);

        var act = () => order.UpdateOpen(null, null, null, null, null, 0m, ShippingAddress.Empty, UserId, Now);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot be edited*");
    }
}
