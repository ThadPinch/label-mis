namespace LabelsMis.Domain.Estimating.Models;

public record FinishingOperationRequest(
    Guid OperationId,
    decimal SetupMinutes,
    decimal RunSpeedFpm,
    decimal CostPerHour,
    string Description,
    // Consumable stock for material-bearing operations (lamination, foil); null for the rest.
    Guid? StockId = null,
    decimal? StockWidthIn = null,
    decimal? StockCostPerMsi = null,
    string? StockLabel = null);
