namespace LabelsMis.Domain.Estimating.Models;

public record FinishingOperationRequest(
    Guid OperationId,
    decimal SetupMinutes,
    decimal RunSpeedFpm,
    decimal CostPerHour,
    string Description);
