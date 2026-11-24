namespace LabelsMis.Domain.Enums;

/// <summary>Which of the carrier's two worlds the saved keys belong to.</summary>
public enum CarrierEnvironment
{
    /// <summary>The carrier's test system: labels are samples, nothing is billed.</summary>
    Staging = 0,
    Production = 1
}
