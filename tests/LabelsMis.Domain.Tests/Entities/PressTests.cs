using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Tests.Entities;

public class PressTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public void CreateIndigo6800_SeedsTheWs6800SeparationSpeeds()
    {
        var press = Press.CreateIndigo6800(UserId, DateTime.UtcNow);

        (press.SpeedFpm3Separations, press.SpeedFpm4Separations, press.SpeedFpm5PlusSeparations)
            .Should().Be((131m, 98m, 72m));
    }

    [Fact]
    public void Create_WithoutSeparationSpeeds_LeavesThemBlank()
    {
        var press = Create();

        press.SpeedFpm3Separations.Should().BeNull();
        press.SpeedFpm4Separations.Should().BeNull();
        press.SpeedFpm5PlusSeparations.Should().BeNull();
    }

    [Fact]
    public void Update_ZeroSeparationSpeed_IsStoredAsBlank()
    {
        var press = Create();

        Update(press, 131m, 0m, null);

        (press.SpeedFpm3Separations, press.SpeedFpm4Separations, press.SpeedFpm5PlusSeparations)
            .Should().Be((131m, (decimal?)null, (decimal?)null));
    }

    [Fact]
    public void Update_NegativeSeparationSpeed_Throws()
    {
        var press = Create();

        var act = () => Update(press, 131m, -1m, 72m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static Press Create() => Press.Create(
        Guid.NewGuid(), "Test press", "TEST", PressType.DigitalInkjet, 13m, 7.87m, 13.39m, 12.59m, 18.9m,
        38.58m, 38.58m, 10m, 2, 7, 98m, 15m, 125m, true, UserId, DateTime.UtcNow);

    private static void Update(Press press, decimal? speed3, decimal? speed4, decimal? speed5Plus) => press.Update(
        press.Name, press.Code, press.PressType, press.WebWidthIn, press.MinWebWidthIn, press.MaxWebWidthIn,
        press.MaxImageWidthIn, press.FrameRepeatIn, press.MaxImageLengthIn, press.MaxRepeatIn, press.MinRepeatIn,
        press.FrameFactor, press.MaxColors, press.SpeedFpm, press.SetupMinutes, press.CostPerHour, press.IsClickBased,
        speed3, speed4, speed5Plus, UserId, DateTime.UtcNow);
}
