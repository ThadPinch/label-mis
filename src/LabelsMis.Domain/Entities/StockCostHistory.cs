using LabelsMis.Domain.Common;

namespace LabelsMis.Domain.Entities;

public class StockCostHistory : EntityBase
{
    private StockCostHistory()
    {
    }

    public Guid StockId { get; private set; }
    public Stock Stock { get; private set; } = null!;
    public decimal CostPerMsi { get; private set; }
    public DateTime EffectiveDate { get; private set; }
    public Guid RecordedById { get; private set; }

    public static StockCostHistory Create(
        Guid id,
        Guid stockId,
        decimal costPerMsi,
        DateTime effectiveDate,
        Guid recordedById,
        DateTime recordedAt)
    {
        if (costPerMsi < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(costPerMsi), "Cost per MSI cannot be negative.");
        }

        var history = new StockCostHistory
        {
            StockId = stockId,
            CostPerMsi = costPerMsi,
            EffectiveDate = effectiveDate,
            RecordedById = recordedById
        };
        history.SetCreated(id, recordedById, recordedAt);
        return history;
    }
}
