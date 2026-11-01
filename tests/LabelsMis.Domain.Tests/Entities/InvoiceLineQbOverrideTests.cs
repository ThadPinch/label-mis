using LabelsMis.Domain.Entities;

namespace LabelsMis.Domain.Tests.Entities;

public class InvoiceLineQbOverrideTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SetQbOverrides_TrimsAndClearsWithBlank()
    {
        var line = InvoiceLine.Create(Guid.NewGuid(), Guid.NewGuid(), 1, null, null, "Die creation", 1, 75m, "TAX", UserId, Now);

        line.SetQbOverrides("  Design ", " Buyout", UserId, Now);
        line.QbItemOverride.Should().Be("Design");
        line.QbClassOverride.Should().Be("Buyout");

        line.SetQbOverrides(" ", null, UserId, Now);
        line.QbItemOverride.Should().BeNull();
        line.QbClassOverride.Should().BeNull();
    }

    [Fact]
    public void SetQbOverrides_RefusesOverlongNames()
    {
        var line = InvoiceLine.Create(Guid.NewGuid(), Guid.NewGuid(), 1, null, null, "Rush", 1, 50m, "TAX", UserId, Now);

        ((Action)(() => line.SetQbOverrides(new string('x', 101), null, UserId, Now))).Should().Throw<ArgumentException>();
    }
}
