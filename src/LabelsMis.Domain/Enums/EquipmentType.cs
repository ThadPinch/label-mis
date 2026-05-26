namespace LabelsMis.Domain.Enums;

/// <summary>
/// Discriminator for JobOperation.EquipmentId — references Press or FinishingOperation by Id.
/// Pack/Ship/Inspection operations use None.
/// </summary>
public enum EquipmentType
{
    None = 0,
    Press = 1,
    FinishingOperation = 2
}
