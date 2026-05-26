using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Tests.Entities;

public class ProductTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public void Discontinue_WithOpenOrderLines_Throws()
    {
        var product = CreateProduct();

        var act = () => product.Discontinue(hasOpenOrderLines: true, UserId, Now);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*open sales orders*");
    }

    [Fact]
    public void Discontinue_WithOpenOrderLinesAndForce_Succeeds()
    {
        var product = CreateProduct();

        product.Discontinue(hasOpenOrderLines: true, UserId, Now, allowWithOpenOrders: true);

        product.Status.Should().Be(ProductStatus.Discontinued);
        product.IsActive.Should().BeFalse();
    }

    private static Product CreateProduct()
    {
        var customerId = Guid.NewGuid();
        return Product.Create(
            Guid.NewGuid(), customerId, [customerId], "ACME-0001", null, "Test product", null,
            4, 3, 0.125m, Guid.NewGuid(), InkSet.CMYK, "[]", null, null, UserId, Now);
    }
}
