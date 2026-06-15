using System.ComponentModel.DataAnnotations;
using LabelsMis.Domain.Enums;
using LabelsMis.Web.Pages.Shared;
using LabelsMis.Web.Services.Inks;

namespace LabelsMis.Web.Pages.Inks;

public class InkFormInput
{
    [Required]
    public string Code { get; set; } = string.Empty;

    [Required]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "Ink set")]
    public InkSet InkSet { get; set; } = InkSet.CMYK;

    [Range(0, 999999)]
    [Display(Name = "Click rate per 1000")]
    public decimal ClickRatePer1000 { get; set; }

    [Display(Name = "Is white")]
    public bool IsWhite { get; set; }

    [Display(Name = "Is silver")]
    public bool IsSilver { get; set; }

    [Range(0, 999999)]
    [Display(Name = "Bottle cost")]
    public decimal BottleCost { get; set; }

    [Range(0, 999999)]
    [Display(Name = "Bottle size (mL)")]
    public decimal BottleSizeMl { get; set; } = 1500m;

    [Range(0, 999999)]
    [Display(Name = "mL per 1000 in² @ 100%")]
    public decimal MlPer1000SqIn { get; set; }

    [Range(0, 100)]
    [Display(Name = "Default coverage %")]
    public decimal DefaultCoveragePct { get; set; } = 100m;

    public InkForm ToForm() => new(
        Code, Description, InkSet, ClickRatePer1000, IsWhite, IsSilver,
        BottleCost, BottleSizeMl, MlPer1000SqIn, DefaultCoveragePct / 100m);

    public static InkFormInput FromEntity(Domain.Entities.Ink ink) => new()
    {
        Code = ink.Code,
        Description = ink.Description,
        InkSet = ink.InkSet,
        ClickRatePer1000 = ink.ClickRatePer1000,
        IsWhite = ink.IsWhite,
        IsSilver = ink.IsSilver,
        BottleCost = ink.BottleCost,
        BottleSizeMl = ink.BottleSizeMl,
        MlPer1000SqIn = ink.MlPer1000SqIn,
        DefaultCoveragePct = ink.DefaultCoveragePct * 100m
    };

    public static InkFormInput ForDuplicate(Domain.Entities.Ink ink)
    {
        var input = FromEntity(ink);
        input.Code = MasterDataDuplicateHelper.DuplicateCode(ink.Code);
        return input;
    }
}
