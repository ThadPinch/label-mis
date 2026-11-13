using System.ComponentModel.DataAnnotations;

namespace LabelsMis.Domain.Enums;

/// <summary>The invoice number QuickBooks gets. The first value is the default for a company that
/// has never chosen.</summary>
public enum QuickBooksInvoiceNumberFormat
{
    /// <summary>The LabelShop number as it is: <c>INV-2026-00549</c>.</summary>
    [Display(Name = "The full LabelShop number")]
    Full = 0,

    /// <summary>Only the number at the end: <c>00549</c>.</summary>
    [Display(Name = "Only the number at the end")]
    NumberOnly = 1
}
