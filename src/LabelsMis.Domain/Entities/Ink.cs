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

    public static Ink Create(
        Guid id,
        string code,
        string description,
        InkSet inkSet,
        decimal clickRatePer1000,
        bool isWhite,
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
            IsWhite = isWhite
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
        SetModified(modifiedById, modifiedAt);
    }
}
