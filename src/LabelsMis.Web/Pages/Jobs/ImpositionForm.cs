using System.ComponentModel.DataAnnotations;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.ValueObjects;
using LabelsMis.Web.Services.Jobs;

namespace LabelsMis.Web.Pages.Jobs;

/// <summary>The imposition template as posted from the job page.</summary>
public class ImpositionForm
{
    [Range(0.0001, 100)] public decimal LabelAcrossIn { get; set; }
    [Range(0.0001, 100)] public decimal LabelAroundIn { get; set; }
    [Range(0, 100)] public decimal CornerRadiusIn { get; set; }
    [Range(0, 100)] public decimal GutterAcrossIn { get; set; }
    [Range(0, 100)] public decimal GutterAroundIn { get; set; }
    [Range(0, 100)] public decimal BleedIn { get; set; }
    [Range(1, 1000)] public int LabelsAcross { get; set; } = 1;
    [Range(1, 1000)] public int LabelsAround { get; set; } = 1;
    public LabelOrientation Orientation { get; set; }
    [Range(0.0001, 100)] public decimal WebWidthIn { get; set; }
    [Range(-100, 100)] public decimal CrossWebOffsetIn { get; set; }
    public ImpositionMarkSide EyeMarks { get; set; }
    [Range(0.0001, 10)] public decimal EyeMarkWidthIn { get; set; } = ImpositionTemplate.DefaultEyeMarkWidthIn;
    [Range(0.0001, 10)] public decimal EyeMarkHeightIn { get; set; } = ImpositionTemplate.DefaultEyeMarkHeightIn;
    public bool IncludeDieLines { get; set; }
    public bool IncludeSlug { get; set; } = true;

    public ImpositionTemplateInput ToInput() => new(
        LabelAcrossIn, LabelAroundIn, CornerRadiusIn, GutterAcrossIn, GutterAroundIn, BleedIn,
        LabelsAcross, LabelsAround, Orientation, WebWidthIn, CrossWebOffsetIn,
        EyeMarks, EyeMarkWidthIn, EyeMarkHeightIn, IncludeDieLines, IncludeSlug);

    public static ImpositionForm From(ImpositionTemplate t) => new()
    {
        LabelAcrossIn = t.LabelAcrossIn,
        LabelAroundIn = t.LabelAroundIn,
        CornerRadiusIn = t.CornerRadiusIn,
        GutterAcrossIn = t.GutterAcrossIn,
        GutterAroundIn = t.GutterAroundIn,
        BleedIn = t.BleedIn,
        LabelsAcross = t.LabelsAcross,
        LabelsAround = t.LabelsAround,
        Orientation = t.Orientation,
        WebWidthIn = t.WebWidthIn,
        CrossWebOffsetIn = t.CrossWebOffsetIn,
        EyeMarks = t.EyeMarks,
        EyeMarkWidthIn = t.EyeMarkWidthIn,
        EyeMarkHeightIn = t.EyeMarkHeightIn,
        IncludeDieLines = t.IncludeDieLines,
        IncludeSlug = t.IncludeSlug
    };
}
