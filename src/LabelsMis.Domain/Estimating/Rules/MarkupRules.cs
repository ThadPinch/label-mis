namespace LabelsMis.Domain.Estimating.Rules;

internal sealed record PricingResult(
    decimal TotalPrice,
    decimal UnitPrice,
    decimal PricePerThousand,
    decimal MarginPct,
    bool BelowMinimumMargin);

internal static class MarkupRules
{
    public static PricingResult Calculate(
        decimal totalCost,
        int quantity,
        decimal customerMarkupPct,
        decimal minimumMarginPct)
    {
        var totalPrice = EstimatingMath.RoundCurrency(totalCost * (1 + customerMarkupPct));
        var unitPrice = quantity > 0
            ? EstimatingMath.RoundMoney(totalPrice / quantity)
            : 0m;
        var pricePerThousand = quantity > 0
            ? EstimatingMath.RoundCurrency((totalPrice / quantity) * 1000m)
            : 0m;

        decimal marginPct;
        if (totalPrice > 0)
        {
            marginPct = EstimatingMath.RoundMoney((totalPrice - totalCost) / totalPrice);
        }
        else
        {
            marginPct = 0m;
        }

        var belowMinimumMargin = marginPct < minimumMarginPct;

        return new PricingResult(
            totalPrice,
            unitPrice,
            pricePerThousand,
            marginPct,
            belowMinimumMargin);
    }
}
