using System.ComponentModel.DataAnnotations;

namespace LabelsMis.Domain.Enums;

/// <summary>
/// Customer payment terms. For the Net terms the integer value is the number of days from invoice
/// date until due. "Due immediately" terms (COD, Due on Receipt, Prepay) use non-positive sentinel
/// values — always convert to due-days via <see cref="PaymentTermsExtensions.ToDueDays"/> rather than
/// casting the enum, since the sentinels are not day counts.
/// </summary>
public enum PaymentTerms
{
    [Display(Name = "Prepay")]
    Prepay = -2,

    [Display(Name = "Due on Receipt")]
    DueOnReceipt = -1,

    [Display(Name = "COD")]
    Cod = 0,

    [Display(Name = "Net 15")]
    Net15 = 15,

    [Display(Name = "Net 30")]
    Net30 = 30,

    [Display(Name = "Net 60")]
    Net60 = 60,

    [Display(Name = "Net 90")]
    Net90 = 90
}

public static class PaymentTermsExtensions
{
    /// <summary>Days from invoice date until the invoice is due. Immediate terms (Prepay, Due on
    /// Receipt, COD) are due the same day (0).</summary>
    public static int ToDueDays(this PaymentTerms terms) => (int)terms < 0 ? 0 : (int)terms;
}

public static class PaymentTermsParser
{
    /// <summary>Best-effort parse of free-text terms (e.g. from CSV import) into <see cref="PaymentTerms"/>.</summary>
    public static PaymentTerms Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return PaymentTerms.Net30;
        }

        var trimmed = text.Trim();
        if (trimmed.Contains("prepay", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("pre-pay", StringComparison.OrdinalIgnoreCase))
        {
            return PaymentTerms.Prepay;
        }

        if (trimmed.Contains("receipt", StringComparison.OrdinalIgnoreCase))
        {
            return PaymentTerms.DueOnReceipt;
        }

        if (trimmed.Contains("COD", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("collect", StringComparison.OrdinalIgnoreCase))
        {
            return PaymentTerms.Cod;
        }

        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        return digits switch
        {
            "15" => PaymentTerms.Net15,
            "60" => PaymentTerms.Net60,
            "90" => PaymentTerms.Net90,
            "0" => PaymentTerms.Cod,
            _ => PaymentTerms.Net30
        };
    }
}
