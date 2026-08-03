namespace LabelsMis.Domain.Enums;

public enum StockType
{
    Substrate = 0,
    Laminate = 1,

    /// <summary>Shrink film: behaves like a substrate but has no liner and carries a layflat spec.</summary>
    Shrink = 2
}
