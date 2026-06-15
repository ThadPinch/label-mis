using System.ComponentModel.DataAnnotations;

namespace LabelsMis.Domain.Enums;

/// <summary>
/// Customer payment terms. The integer value is the number of days from invoice date until the
/// invoice is due, so due-date math can use the enum value directly (COD = due immediately).
/// </summary>
public enum PaymentTerms
{
    [Display(Name = "COD")]
    Cod = 0,

    [Display(Name = "Net 15")]
    Net15 = 15,

    [Display(Name = "Net 30")]
    Net30 = 30,

    [Display(Name = "Net 60")]
    Net60 = 60
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
            "0" => PaymentTerms.Cod,
            _ => PaymentTerms.Net30
        };
    }
}
