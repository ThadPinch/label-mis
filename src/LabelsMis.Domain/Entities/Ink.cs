using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Entities;

public class Ink : MasterDataEntity
{
    private Ink()
    {
    }

    public string Code { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public InkSet InkSet { get; private set; }
    public decimal ClickRatePer1000 { get; private set; }

    // A spot ink (white, silver, or a named PMS-style color). Process inks have IsSpot = false.
    public bool IsSpot { get; private set; }
    public SpotColor? SpotColor { get; private set; }

    // Bottle/coverage costing for spot inks (white, silver, PMS spots).
    public decimal BottleCost { get; private set; }
    public decimal BottleSizeMl { get; private set; }
    public decimal MlPer1000SqIn { get; private set; }
    public decimal DefaultCoveragePct { get; private set; } = 1m;

    // Optional press-speed overrides (fpm) for spot inks by hit count.
    // When set, the slowest applicable speed governs the estimate's press run time.
    public decimal? SpeedFpm1Hit { get; private set; }
    public decimal? SpeedFpm2Hit { get; private set; }
    public decimal? SpeedFpm3Hit { get; private set; }

    // The white spot ink drives the estimate's White hits control on CMYKW ink sets.
    public bool IsWhiteSpot => IsSpot && SpotColor == Enums.SpotColor.White;

    public static Ink Create(
        Guid id,
        string code,
        string description,
        InkSet inkSet,
        decimal clickRatePer1000,
        bool isSpot,
        SpotColor? spotColor,
        decimal bottleCost,
        decimal bottleSizeMl,
        decimal mlPer1000SqIn,
        decimal defaultCoveragePct,
        Guid createdById,
        DateTime createdAt,
        decimal? speedFpm1Hit = null,
        decimal? speedFpm2Hit = null,
        decimal? speedFpm3Hit = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Ink code is required.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Ink description is required.", nameof(description));
        }

        if (clickRatePer1000 < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(clickRatePer1000), "Click rate cannot be negative.");
        }

        ValidateSpot(isSpot, spotColor);
        ValidateSpeedOverrides(speedFpm1Hit, speedFpm2Hit, speedFpm3Hit);

        var ink = new Ink
        {
            Code = code.Trim().ToUpperInvariant(),
            Description = description.Trim(),
            InkSet = inkSet,
            ClickRatePer1000 = clickRatePer1000,
            IsSpot = isSpot,
            SpotColor = isSpot ? spotColor : null,
            BottleCost = bottleCost,
            BottleSizeMl = bottleSizeMl,
            MlPer1000SqIn = mlPer1000SqIn,
            DefaultCoveragePct = defaultCoveragePct <= 0 ? 1m : defaultCoveragePct,
            SpeedFpm1Hit = NormalizeSpeed(speedFpm1Hit),
            SpeedFpm2Hit = NormalizeSpeed(speedFpm2Hit),
            SpeedFpm3Hit = NormalizeSpeed(speedFpm3Hit)
        };
        ink.SetCreated(id, createdById, createdAt);
        return ink;
    }

    public void Update(
        string code,
        string description,
        InkSet inkSet,
        decimal clickRatePer1000,
        bool isSpot,
        SpotColor? spotColor,
        decimal bottleCost,
        decimal bottleSizeMl,
        decimal mlPer1000SqIn,
        decimal defaultCoveragePct,
        Guid modifiedById,
        DateTime modifiedAt,
        decimal? speedFpm1Hit = null,
        decimal? speedFpm2Hit = null,
        decimal? speedFpm3Hit = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Ink code is required.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Ink description is required.", nameof(description));
        }

        if (clickRatePer1000 < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(clickRatePer1000), "Click rate cannot be negative.");
        }

        ValidateSpot(isSpot, spotColor);
        ValidateSpeedOverrides(speedFpm1Hit, speedFpm2Hit, speedFpm3Hit);

        Code = code.Trim().ToUpperInvariant();
        Description = description.Trim();
        InkSet = inkSet;
        ClickRatePer1000 = clickRatePer1000;
        IsSpot = isSpot;
        SpotColor = isSpot ? spotColor : null;
        BottleCost = bottleCost;
        BottleSizeMl = bottleSizeMl;
        MlPer1000SqIn = mlPer1000SqIn;
        DefaultCoveragePct = defaultCoveragePct <= 0 ? 1m : defaultCoveragePct;
        SpeedFpm1Hit = NormalizeSpeed(speedFpm1Hit);
        SpeedFpm2Hit = NormalizeSpeed(speedFpm2Hit);
        SpeedFpm3Hit = NormalizeSpeed(speedFpm3Hit);
        SetModified(modifiedById, modifiedAt);
    }

    private static void ValidateSpot(bool isSpot, SpotColor? spotColor)
    {
        if (isSpot && spotColor is null)
        {
            throw new ArgumentException("A spot ink must have a spot color.", nameof(spotColor));
        }
    }

    private static void ValidateSpeedOverrides(decimal? speed1, decimal? speed2, decimal? speed3)
    {
        if (speed1 < 0 || speed2 < 0 || speed3 < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(speed1), "Press speed override cannot be negative.");
        }
    }

    // Treat 0 (or blank) as "no override" so an empty form field does not force the press to 0 fpm.
    private static decimal? NormalizeSpeed(decimal? speed) =>
        speed is > 0 ? speed : null;
}
