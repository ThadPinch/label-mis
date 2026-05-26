namespace LabelsMis.Domain.Entities;

public record EstimateFinishingOperationSelection(
    Guid OperationId,
    decimal? SetupMinutesOverride,
    decimal? RunSpeedFpmOverride,
    int SortOrder);
