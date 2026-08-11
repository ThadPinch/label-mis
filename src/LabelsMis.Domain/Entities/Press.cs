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
    public decimal MinWebWidthIn { get; private set; }
    public decimal MaxWebWidthIn { get; private set; }
    public decimal MaxImageWidthIn { get; private set; }
    public decimal FrameRepeatIn { get; private set; }
    public decimal MaxImageLengthIn { get; private set; }
    public decimal MaxRepeatIn { get; private set; }
    public decimal MinRepeatIn { get; private set; }

    /// <summary>Click charge units per impression (HP frame factor — a full Indigo frame bills as this many clicks per separation).</summary>
    public int FrameFactor { get; private set; }

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
        decimal minWebWidthIn,
        decimal maxWebWidthIn,
        decimal maxImageWidthIn,
        decimal frameRepeatIn,
        decimal maxImageLengthIn,
        decimal maxRepeatIn,
        decimal minRepeatIn,
        int frameFactor,
        int maxColors,
        decimal speedFpm,
        decimal setupMinutes,
        decimal costPerHour,
        bool isClickBased,
        Guid createdById,
        DateTime createdAt)
    {
        Validate(
            name,
            code,
            webWidthIn,
            minWebWidthIn,
            maxWebWidthIn,
            maxImageWidthIn,
            frameRepeatIn,
            maxImageLengthIn,
            maxRepeatIn,
            minRepeatIn,
            frameFactor,
            maxColors,
            speedFpm,
            setupMinutes,
            costPerHour);

        var press = new Press
        {
            Name = name.Trim(),
            Code = code.Trim().ToUpperInvariant(),
            PressType = pressType,
            WebWidthIn = webWidthIn,
            MinWebWidthIn = minWebWidthIn,
            MaxWebWidthIn = maxWebWidthIn,
            MaxImageWidthIn = maxImageWidthIn,
            FrameRepeatIn = frameRepeatIn,
            MaxImageLengthIn = maxImageLengthIn,
            MaxRepeatIn = maxRepeatIn,
            MinRepeatIn = minRepeatIn,
            FrameFactor = frameFactor,
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
            "HP Indigo WS6800",
            "INDIGO6800",
            PressType.DigitalInkjet,
            webWidthIn: 13.39m,
            minWebWidthIn: 7.87m,
            maxWebWidthIn: 13.39m,
            maxImageWidthIn: 12.59m,
            frameRepeatIn: 18.9m,
            maxImageLengthIn: 38.58m,
            maxRepeatIn: 38.58m,
            minRepeatIn: 18.9m,
            frameFactor: 2,
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
        decimal minWebWidthIn,
        decimal maxWebWidthIn,
        decimal maxImageWidthIn,
        decimal frameRepeatIn,
        decimal maxImageLengthIn,
        decimal maxRepeatIn,
        decimal minRepeatIn,
        int frameFactor,
        int maxColors,
        decimal speedFpm,
        decimal setupMinutes,
        decimal costPerHour,
        bool isClickBased,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        Validate(
            name,
            code,
            webWidthIn,
            minWebWidthIn,
            maxWebWidthIn,
            maxImageWidthIn,
            frameRepeatIn,
            maxImageLengthIn,
            maxRepeatIn,
            minRepeatIn,
            frameFactor,
            maxColors,
            speedFpm,
            setupMinutes,
            costPerHour);

        Name = name.Trim();
        Code = code.Trim().ToUpperInvariant();
        PressType = pressType;
        WebWidthIn = webWidthIn;
        MinWebWidthIn = minWebWidthIn;
        MaxWebWidthIn = maxWebWidthIn;
        MaxImageWidthIn = maxImageWidthIn;
        FrameRepeatIn = frameRepeatIn;
        MaxImageLengthIn = maxImageLengthIn;
        MaxRepeatIn = maxRepeatIn;
        MinRepeatIn = minRepeatIn;
        FrameFactor = frameFactor;
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
        decimal minWebWidthIn,
        decimal maxWebWidthIn,
        decimal maxImageWidthIn,
        decimal frameRepeatIn,
        decimal maxImageLengthIn,
        decimal maxRepeatIn,
        decimal minRepeatIn,
        int frameFactor,
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

        if (minWebWidthIn <= 0 || maxWebWidthIn <= 0 || minWebWidthIn > maxWebWidthIn)
        {
            throw new ArgumentOutOfRangeException(nameof(minWebWidthIn), "Web width range is invalid.");
        }

        if (maxImageWidthIn <= 0 || maxImageWidthIn > maxWebWidthIn)
        {
            throw new ArgumentOutOfRangeException(nameof(maxImageWidthIn), "Max image width is invalid.");
        }

        if (frameRepeatIn <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameRepeatIn), "Frame repeat must be greater than zero.");
        }

        if (maxImageLengthIn < frameRepeatIn)
        {
            throw new ArgumentOutOfRangeException(nameof(maxImageLengthIn), "Max image length must be at least one frame repeat.");
        }

        if (minRepeatIn <= 0 || maxRepeatIn <= 0 || minRepeatIn > maxRepeatIn)
        {
            throw new ArgumentOutOfRangeException(nameof(minRepeatIn), "Repeat range is invalid.");
        }

        if (frameFactor < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(frameFactor), "Frame factor must be at least one.");
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
