using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Tests.Entities;

public class DieTests
{
    [Fact]
    public void Create_StoresEnteredLabelsAcrossAndDerivesRepeatLength()
    {
        var die = Die.Create(
            Guid.NewGuid(),
            "4x3 rectangle",
            customerId: null,
            DieType.Flexible,
            shape: "Rectangle",
            labelAcrossIn: 4.0m,
            labelAroundIn: 3.0m,
            cornerRadiusIn: 0.125m,
            gutterAcrossIn: 0.0625m,
            gutterAroundIn: 0.0625m,
            labelsAcross: 3,
            labelsAround: 1,
            webWidthIn: 13.0m,
            supplierId: null,
            supplierPartNumber: null,
            location: "A1",
            createdById: Guid.NewGuid(),
            createdAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        die.LabelsAcross.Should().Be(3);
        die.RepeatLengthIn.Should().Be(3.0625m);
    }

    [Theory]
    [InlineData(13.0, 0.0625, 4.0, 3)]
    [InlineData(13.0, 0.0625, 6.4375, 2)]
    public void CalculateLabelsAcross_MatchesExpected(decimal webWidth, decimal gutter, decimal labelAcross, int expected)
    {
        Die.CalculateLabelsAcross(webWidth, gutter, labelAcross).Should().Be(expected);
    }

    [Fact]
    public void UpdateImposition_StoresEnteredLabelsAcross()
    {
        var die = Die.Create(
            Guid.NewGuid(),
            "Test die",
            null,
            DieType.Solid,
            null,
            4.0m,
            3.0m,
            0m,
            0.0625m,
            0.0625m,
            3,
            1,
            13.0m,
            null,
            null,
            null,
            Guid.NewGuid(),
            DateTime.UtcNow);

        die.LabelsAcross.Should().Be(3);

        die.UpdateImposition(4.0m, 3.0m, 0m, 0.0625m, 0.0625m, 1, 1, 6.0m, Guid.NewGuid(), DateTime.UtcNow);

        die.LabelsAcross.Should().Be(1);
    }
}
