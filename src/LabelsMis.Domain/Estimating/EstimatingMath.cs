namespace LabelsMis.Domain.Estimating;

internal static class EstimatingMath
{
    public static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    public static decimal RoundCurrency(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal RoundOneDecimal(decimal value) =>
        decimal.Round(value, 1, MidpointRounding.AwayFromZero);

    public static decimal RoundUpOneDecimal(decimal value) =>
        Math.Ceiling(value * 10m) / 10m;

    public static decimal RoundTwoDecimals(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    public static int CeilingDivision(decimal numerator, decimal denominator) =>
        (int)Math.Ceiling(numerator / denominator);
}
