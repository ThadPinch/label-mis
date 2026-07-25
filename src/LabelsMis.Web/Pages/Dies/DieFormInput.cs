using System.ComponentModel.DataAnnotations;
using LabelsMis.Domain.Enums;
using LabelsMis.Web.Pages.Shared;
using LabelsMis.Web.Services.Dies;

namespace LabelsMis.Web.Pages.Dies;

public class DieFormInput
{
    [Required]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "Customer")]
    public Guid? CustomerId { get; set; }

    [Display(Name = "Die type")]
    public DieType DieType { get; set; } = DieType.Flexible;

    public string? Shape { get; set; }

    [Range(0.0001, 100)]
    [Display(Name = "Label across (in)")]
    public decimal LabelAcrossIn { get; set; }

    [Range(0.0001, 100)]
    [Display(Name = "Label around (in)")]
    public decimal LabelAroundIn { get; set; }

    [Range(0, 100)]
    [Display(Name = "Corner radius (in)")]
    public decimal CornerRadiusIn { get; set; }

    [Range(0, 100)]
    [Display(Name = "Gutter across (in)")]
    public decimal GutterAcrossIn { get; set; }

    [Range(0, 100)]
    [Display(Name = "Gutter around (in)")]
    public decimal GutterAroundIn { get; set; }

    [Range(1, 1000)]
    [Display(Name = "Labels across")]
    public int LabelsAcross { get; set; } = 1;

    [Range(1, 1000)]
    [Display(Name = "Labels around")]
    public int LabelsAround { get; set; } = 1;

    [Range(0.0001, 100)]
    [Display(Name = "Web width (in)")]
    public decimal WebWidthIn { get; set; }

    [Display(Name = "Supplier")]
    public Guid? SupplierId { get; set; }

    [Display(Name = "Supplier part number")]
    public string? SupplierPartNumber { get; set; }

    public string? Location { get; set; }

    public DieForm ToForm() => new(
        Description,
        CustomerId,
        DieType,
        Shape,
        LabelAcrossIn,
        LabelAroundIn,
        CornerRadiusIn,
        GutterAcrossIn,
        GutterAroundIn,
        LabelsAcross,
        LabelsAround,
        WebWidthIn,
        SupplierId,
        SupplierPartNumber,
        Location);

    public static DieFormInput FromEntity(Domain.Entities.Die die) => new()
    {
        Description = die.Description,
        CustomerId = die.CustomerId,
        DieType = die.DieType,
        Shape = die.Shape,
        LabelAcrossIn = die.LabelAcrossIn,
        LabelAroundIn = die.LabelAroundIn,
        CornerRadiusIn = die.CornerRadiusIn,
        GutterAcrossIn = die.GutterAcrossIn,
        GutterAroundIn = die.GutterAroundIn,
        LabelsAcross = die.LabelsAcross,
        LabelsAround = die.LabelsAround,
        WebWidthIn = die.WebWidthIn,
        SupplierId = die.SupplierId,
        SupplierPartNumber = die.SupplierPartNumber,
        Location = die.Location
    };

    public static DieFormInput ForDuplicate(Domain.Entities.Die die)
    {
        var input = FromEntity(die);
        input.Description = MasterDataDuplicateHelper.DuplicateDescription(die.Description);
        return input;
    }
}
