namespace LabelsMis.Domain.Inventory;

public static class RollSplitValidator
{
    public static void ValidateWidths(decimal originalWidthIn, IReadOnlyList<decimal> childWidthsIn)
    {
        if (childWidthsIn.Count < 2)
        {
            throw new ArgumentException("Split requires at least two child rolls.");
        }

        if (childWidthsIn.Any(w => w <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(childWidthsIn), "Child widths must be positive.");
        }

        var total = childWidthsIn.Sum();
        if (Math.Abs(total - originalWidthIn) > 0.0001m)
        {
            throw new InvalidOperationException(
                $"Split widths ({total}\") must equal original width ({originalWidthIn}\").");
        }
    }
}

public static class RollReconciliation
{
    public record RollReconciliationResult(
        Guid StockId,
        decimal TotalReceivedLf,
        decimal TotalRemainingLf,
        decimal TotalConsumedLf,
        decimal DiscrepancyLf,
        bool IsBalanced);

    public static RollReconciliationResult Calculate(
        Guid stockId,
        decimal totalReceivedLf,
        decimal totalRemainingLf,
        decimal totalConsumedLf,
        decimal toleranceLf = 0.01m)
    {
        var discrepancy = totalReceivedLf - (totalRemainingLf + totalConsumedLf);
        return new RollReconciliationResult(
            stockId,
            totalReceivedLf,
            totalRemainingLf,
            totalConsumedLf,
            discrepancy,
            Math.Abs(discrepancy) <= toleranceLf);
    }
}
