using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Entities;

public class FinishingOperation : MasterDataEntity
{
    private FinishingOperation()
    {
    }

    public string Code { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public FinishingOperationType OperationType { get; private set; }
    public Guid? DieId { get; private set; }
    public Die? Die { get; private set; }
    public decimal DefaultSetupMinutes { get; private set; }
    public decimal DefaultRunSpeedFpm { get; private set; }
    public string EquipmentName { get; private set; } = string.Empty;
    public decimal CostPerHour { get; private set; }

    public static FinishingOperation Create(
        Guid id,
        string code,
        string description,
        FinishingOperationType operationType,
        Guid? dieId,
        decimal defaultSetupMinutes,
        decimal defaultRunSpeedFpm,
        string equipmentName,
        decimal costPerHour,
        Guid createdById,
        DateTime createdAt)
    {
        Validate(code, description, equipmentName, operationType, dieId, defaultSetupMinutes, defaultRunSpeedFpm, costPerHour);

        var operation = new FinishingOperation
        {
            Code = code.Trim().ToUpperInvariant(),
            Description = description.Trim(),
            OperationType = operationType,
            DieId = ResolveDieId(operationType, dieId),
            DefaultSetupMinutes = defaultSetupMinutes,
            DefaultRunSpeedFpm = defaultRunSpeedFpm,
            EquipmentName = equipmentName.Trim(),
            CostPerHour = costPerHour
        };
        operation.SetCreated(id, createdById, createdAt);
        return operation;
    }

    public void Update(
        string code,
        string description,
        FinishingOperationType operationType,
        Guid? dieId,
        decimal defaultSetupMinutes,
        decimal defaultRunSpeedFpm,
        string equipmentName,
        decimal costPerHour,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        Validate(code, description, equipmentName, operationType, dieId, defaultSetupMinutes, defaultRunSpeedFpm, costPerHour);

        Code = code.Trim().ToUpperInvariant();
        Description = description.Trim();
        OperationType = operationType;
        DieId = ResolveDieId(operationType, dieId);
        DefaultSetupMinutes = defaultSetupMinutes;
        DefaultRunSpeedFpm = defaultRunSpeedFpm;
        EquipmentName = equipmentName.Trim();
        CostPerHour = costPerHour;
        SetModified(modifiedById, modifiedAt);
    }

    private static Guid? ResolveDieId(FinishingOperationType operationType, Guid? dieId) =>
        operationType == FinishingOperationType.DieCut ? dieId : null;

    private static void Validate(
        string code,
        string description,
        string equipmentName,
        FinishingOperationType operationType,
        Guid? dieId,
        decimal defaultSetupMinutes,
        decimal defaultRunSpeedFpm,
        decimal costPerHour)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Operation code is required.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Operation description is required.", nameof(description));
        }

        if (string.IsNullOrWhiteSpace(equipmentName))
        {
            throw new ArgumentException("Equipment name is required.", nameof(equipmentName));
        }

        if (defaultSetupMinutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultSetupMinutes), "Setup minutes cannot be negative.");
        }

        if (defaultRunSpeedFpm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultRunSpeedFpm), "Run speed must be greater than zero.");
        }

        if (costPerHour < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(costPerHour), "Cost per hour cannot be negative.");
        }

        if (operationType == FinishingOperationType.DieCut && (dieId is null || dieId == Guid.Empty))
        {
            throw new ArgumentException("Die-cut operations must be linked to a die.", nameof(dieId));
        }
    }
}
