using System.ComponentModel.DataAnnotations;
using LabelsMis.Domain.Enums;
using LabelsMis.Web.Pages.Shared;
using LabelsMis.Web.Services.FinishingOperations;

namespace LabelsMis.Web.Pages.FinishingOperations;

public class FinishingOperationFormInput
{
    [Required]
    public string Code { get; set; } = string.Empty;

    [Required]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "Operation type")]
    public FinishingOperationType OperationType { get; set; } = FinishingOperationType.Laminate;

    [Range(0, 999999)]
    [Display(Name = "Default setup (minutes)")]
    public decimal DefaultSetupMinutes { get; set; }

    [Range(0, 999999)]
    [Display(Name = "Default run speed (fpm)")]
    public decimal DefaultRunSpeedFpm { get; set; }

    [Required]
    [Display(Name = "Equipment name")]
    public string EquipmentName { get; set; } = string.Empty;

    [Range(0, 999999)]
    [Display(Name = "Cost per hour")]
    public decimal CostPerHour { get; set; }

    public FinishingOperationForm ToForm() => new(
        Code,
        Description,
        OperationType,
        DefaultSetupMinutes,
        DefaultRunSpeedFpm,
        EquipmentName,
        CostPerHour);

    public static FinishingOperationFormInput FromEntity(Domain.Entities.FinishingOperation operation) => new()
    {
        Code = operation.Code,
        Description = operation.Description,
        OperationType = operation.OperationType,
        DefaultSetupMinutes = operation.DefaultSetupMinutes,
        DefaultRunSpeedFpm = operation.DefaultRunSpeedFpm,
        EquipmentName = operation.EquipmentName,
        CostPerHour = operation.CostPerHour
    };

    public static FinishingOperationFormInput ForDuplicate(Domain.Entities.FinishingOperation operation)
    {
        var input = FromEntity(operation);
        input.Code = MasterDataDuplicateHelper.DuplicateCode(operation.Code);
        return input;
    }
}
