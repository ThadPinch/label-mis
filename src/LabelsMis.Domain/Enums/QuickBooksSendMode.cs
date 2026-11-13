using System.ComponentModel.DataAnnotations;

namespace LabelsMis.Domain.Enums;

/// <summary>What happens to an invoice's email once it is in QuickBooks. The first value is the
/// default for a company that has never chosen.</summary>
public enum QuickBooksSendMode
{
    /// <summary>Flag it "send later": it waits in QuickBooks' own send list for a person.</summary>
    [Display(Name = "Mark it \"send later\" in QuickBooks")]
    SendLater = 0,

    /// <summary>Create it and leave the emailing to whoever opens it in QuickBooks.</summary>
    [Display(Name = "Don't send")]
    Leave = 1,

    /// <summary>QuickBooks emails it to the billing address as soon as it is created.</summary>
    [Display(Name = "Email it from QuickBooks straight away")]
    SendNow = 2
}
