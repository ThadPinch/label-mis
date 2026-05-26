using System.ComponentModel.DataAnnotations;
using LabelsMis.Domain.Enums;
using LabelsMis.Web.Services.Presses;

namespace LabelsMis.Web.Pages.Presses;

public class PressFormInput
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Code { get; set; } = string.Empty;

    [Display(Name = "Press type")]
    public PressType PressType { get; set; } = PressType.DigitalToner;

    [Range(0.0001, 100)]
    [Display(Name = "Web width (in)")]
    public decimal WebWidthIn { get; set; }

    [Range(0.0001, 100)]
    [Display(Name = "Max repeat (in)")]
    public decimal MaxRepeatIn { get; set; }

    [Range(0.0001, 100)]
    [Display(Name = "Min repeat (in)")]
    public decimal MinRepeatIn { get; set; }

    [Range(1, 20)]
    [Display(Name = "Max colors")]
    public int MaxColors { get; set; } = 4;

    [Range(0, 999999)]
    [Display(Name = "Speed (fpm)")]
    public decimal SpeedFpm { get; set; }

    [Range(0, 999999)]
    [Display(Name = "Setup (minutes)")]
    public decimal SetupMinutes { get; set; }

    [Range(0, 999999)]
    [Display(Name = "Cost per hour")]
    public decimal CostPerHour { get; set; }

    [Display(Name = "Click-based pricing")]
    public bool IsClickBased { get; set; } = true;

    public PressForm ToForm() => new(
        Name,
        Code,
        PressType,
        WebWidthIn,
        MaxRepeatIn,
        MinRepeatIn,
        MaxColors,
        SpeedFpm,
        SetupMinutes,
        CostPerHour,
        IsClickBased);

    public static PressFormInput FromEntity(Domain.Entities.Press press) => new()
    {
        Name = press.Name,
        Code = press.Code,
        PressType = press.PressType,
        WebWidthIn = press.WebWidthIn,
        MaxRepeatIn = press.MaxRepeatIn,
        MinRepeatIn = press.MinRepeatIn,
        MaxColors = press.MaxColors,
        SpeedFpm = press.SpeedFpm,
        SetupMinutes = press.SetupMinutes,
        CostPerHour = press.CostPerHour,
        IsClickBased = press.IsClickBased
    };
}
