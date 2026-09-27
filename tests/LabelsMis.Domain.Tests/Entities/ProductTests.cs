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

    [Fact]
    public void Create_TrimsNotes_AndBlankBecomesNull()
    {
        var customerId = Guid.NewGuid();
        var withNotes = Product.Create(
            Guid.NewGuid(), customerId, [customerId], "ACME-0002", null, "Noted product", null,
            4, 3, 0.125m, Guid.NewGuid(), InkSet.CMYK, "[]", null, null, "  Perf every label  ", UserId, Now);
        var blank = Product.Create(
            Guid.NewGuid(), customerId, [customerId], "ACME-0003", null, "Quiet product", null,
            4, 3, 0.125m, Guid.NewGuid(), InkSet.CMYK, "[]", null, null, "   ", UserId, Now);

        withNotes.Notes.Should().Be("Perf every label");
        blank.Notes.Should().BeNull();
    }

    [Fact]
    public void Update_ReplacesNotes()
    {
        var product = CreateProduct();

        product.Update(null, "Test product", 4, 3, 0.125m, product.SubstrateId, InkSet.CMYK, "[]", null, null,
            " Rewind #4 ", UserId, Now);

        product.Notes.Should().Be("Rewind #4");
    }

    [Fact]
    public void ClearArtworkFile_RemovesReferenceAndStampsModified()
    {
        var product = CreateProduct();
        product.SetArtworkFile("artwork/p/20260101.pdf", "label.pdf", UserId, Now);
        var remover = Guid.NewGuid();

        product.ClearArtworkFile(remover, Now.AddMinutes(1));

        product.ArtworkFilePath.Should().BeNull();
        product.ArtworkOriginalFileName.Should().BeNull();
        product.ModifiedById.Should().Be(remover);
        product.ModifiedAt.Should().Be(Now.AddMinutes(1));
    }

    private static Product CreateProduct()
    {
        var customerId = Guid.NewGuid();
        return Product.Create(
            Guid.NewGuid(), customerId, [customerId], "ACME-0001", null, "Test product", null,
            4, 3, 0.125m, Guid.NewGuid(), InkSet.CMYK, "[]", null, null, null, UserId, Now);
    }
}
