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
    public bool IsWhite { get; private set; }
    public bool IsSilver { get; private set; }

    // Bottle/coverage costing for special inks (white, silver, PMS spots).
    public decimal BottleCost { get; private set; }
    public decimal BottleSizeMl { get; private set; }
    public decimal MlPer1000SqIn { get; private set; }
    public decimal DefaultCoveragePct { get; private set; } = 1m;

    public static Ink Create(
        Guid id,
        string code,
        string description,
        InkSet inkSet,
        decimal clickRatePer1000,
        bool isWhite,
        bool isSilver,
        decimal bottleCost,
        decimal bottleSizeMl,
        decimal mlPer1000SqIn,
        decimal defaultCoveragePct,
        Guid createdById,
        DateTime createdAt)
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

        var ink = new Ink
        {
            Code = code.Trim().ToUpperInvariant(),
            Description = description.Trim(),
            InkSet = inkSet,
            ClickRatePer1000 = clickRatePer1000,
            IsWhite = isWhite,
            IsSilver = isSilver,
            BottleCost = bottleCost,
            BottleSizeMl = bottleSizeMl,
            MlPer1000SqIn = mlPer1000SqIn,
            DefaultCoveragePct = defaultCoveragePct <= 0 ? 1m : defaultCoveragePct
        };
        ink.SetCreated(id, createdById, createdAt);
        return ink;
    }

    public void Update(
        string code,
        string description,
        InkSet inkSet,
        decimal clickRatePer1000,
        bool isWhite,
        bool isSilver,
        decimal bottleCost,
        decimal bottleSizeMl,
        decimal mlPer1000SqIn,
        decimal defaultCoveragePct,
        Guid modifiedById,
        DateTime modifiedAt)
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

        Code = code.Trim().ToUpperInvariant();
        Description = description.Trim();
        InkSet = inkSet;
        ClickRatePer1000 = clickRatePer1000;
        IsWhite = isWhite;
        IsSilver = isSilver;
        BottleCost = bottleCost;
        BottleSizeMl = bottleSizeMl;
        MlPer1000SqIn = mlPer1000SqIn;
        DefaultCoveragePct = defaultCoveragePct <= 0 ? 1m : defaultCoveragePct;
        SetModified(modifiedById, modifiedAt);
    }
}
