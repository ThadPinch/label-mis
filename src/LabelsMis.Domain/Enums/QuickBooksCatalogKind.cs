namespace LabelsMis.Domain.Enums;

/// <summary>Which of QuickBooks' lists a cached entry comes from.</summary>
public enum QuickBooksCatalogKind
{
    /// <summary>Products and services.</summary>
    Item = 1,
    Class = 2,

    /// <summary>Payment terms.</summary>
    Term = 3
}
