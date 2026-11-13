using LabelsMis.Domain.Entities;

namespace LabelsMis.Domain.Tests.Entities;

public class DocumentSequenceTests
{
    [Fact]
    public void NextFormattedNumber_OfATypeNeverNumbered_StartsAtOne()
    {
        var sequence = DocumentSequence.Create(Guid.NewGuid(), "INV", 2026);

        sequence.NextFormattedNumber("INV").Should().Be("INV-2026-00001");
        sequence.NextFormattedNumber("INV").Should().Be("INV-2026-00002");
    }

    [Fact]
    public void NextFormattedNumber_OfAYearStartedAtLastYearsNumber_CarriesOn()
    {
        var sequence = DocumentSequence.Create(Guid.NewGuid(), "INV", 2027, lastNumber: 549);

        sequence.NextFormattedNumber("INV").Should().Be("INV-2027-00550");
        sequence.LastNumber.Should().Be(550);
    }

    [Fact]
    public void NextFormattedNumber_PastFiveDigits_KeepsEveryDigit()
    {
        var sequence = DocumentSequence.Create(Guid.NewGuid(), "JOB", 2031, lastNumber: 99999);

        sequence.NextFormattedNumber("JOB").Should().Be("JOB-2031-100000");
    }

    [Fact]
    public void Create_WithANegativeStart_IsRefused()
    {
        var act = () => DocumentSequence.Create(Guid.NewGuid(), "INV", 2027, lastNumber: -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
