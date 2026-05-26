using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Entities;

public class Press : MasterDataEntity
{
    public static readonly Guid Indigo6800Id = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private Press()
    {
    }

    public string Name { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public PressType PressType { get; private set; }
    public decimal WebWidthIn { get; private set; }
    public decimal MaxRepeatIn { get; private set; }
    public decimal MinRepeatIn { get; private set; }
    public int MaxColors { get; private set; }
    public decimal SpeedFpm { get; private set; }
    public decimal SetupMinutes { get; private set; }
    public decimal CostPerHour { get; private set; }
    public bool IsClickBased { get; private set; }

    public static Press Create(
        Guid id,
        string name,
        string code,
        PressType pressType,
        decimal webWidthIn,
        decimal maxRepeatIn,
        decimal minRepeatIn,
        int maxColors,
        decimal speedFpm,
        decimal setupMinutes,
        decimal costPerHour,
        bool isClickBased,
        Guid createdById,
        DateTime createdAt)
    {
        Validate(name, code, webWidthIn, maxRepeatIn, minRepeatIn, maxColors, speedFpm, setupMinutes, costPerHour);

        var press = new Press
        {
            Name = name.Trim(),
            Code = code.Trim().ToUpperInvariant(),
            PressType = pressType,
            WebWidthIn = webWidthIn,
            MaxRepeatIn = maxRepeatIn,
            MinRepeatIn = minRepeatIn,
            MaxColors = maxColors,
            SpeedFpm = speedFpm,
            SetupMinutes = setupMinutes,
            CostPerHour = costPerHour,
            IsClickBased = isClickBased
        };
        press.SetCreated(id, createdById, createdAt);
        return press;
    }

    public static Press CreateIndigo6800(Guid createdById, DateTime createdAt) =>
        Create(
            Indigo6800Id,
            "HP Indigo 6800",
            "INDIGO6800",
            PressType.DigitalInkjet,
            webWidthIn: 13.0m,
            maxRepeatIn: 29.0m,
            minRepeatIn: 3.0m,
            maxColors: 7,
            speedFpm: 100m,
            setupMinutes: 20m,
            costPerHour: 150m,
            isClickBased: true,
            createdById,
            createdAt);

    public void Update(
        string name,
        string code,
        PressType pressType,
        decimal webWidthIn,
        decimal maxRepeatIn,
        decimal minRepeatIn,
        int maxColors,
        decimal speedFpm,
        decimal setupMinutes,
        decimal costPerHour,
        bool isClickBased,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        Validate(name, code, webWidthIn, maxRepeatIn, minRepeatIn, maxColors, speedFpm, setupMinutes, costPerHour);

        Name = name.Trim();
        Code = code.Trim().ToUpperInvariant();
        PressType = pressType;
        WebWidthIn = webWidthIn;
        MaxRepeatIn = maxRepeatIn;
        MinRepeatIn = minRepeatIn;
        MaxColors = maxColors;
        SpeedFpm = speedFpm;
        SetupMinutes = setupMinutes;
        CostPerHour = costPerHour;
        IsClickBased = isClickBased;
        SetModified(modifiedById, modifiedAt);
    }

    private static void Validate(
        string name,
        string code,
        decimal webWidthIn,
        decimal maxRepeatIn,
        decimal minRepeatIn,
        int maxColors,
        decimal speedFpm,
        decimal setupMinutes,
        decimal costPerHour)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Press name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Press code is required.", nameof(code));
        }

        if (webWidthIn <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(webWidthIn), "Web width must be greater than zero.");
        }

        if (minRepeatIn <= 0 || maxRepeatIn <= 0 || minRepeatIn > maxRepeatIn)
        {
            throw new ArgumentOutOfRangeException(nameof(minRepeatIn), "Repeat range is invalid.");
        }

        if (maxColors <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxColors), "Max colors must be at least one.");
        }

        if (speedFpm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(speedFpm), "Speed must be greater than zero.");
        }

        if (setupMinutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(setupMinutes), "Setup minutes cannot be negative.");
        }

        if (costPerHour < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(costPerHour), "Cost per hour cannot be negative.");
        }
    }
}
