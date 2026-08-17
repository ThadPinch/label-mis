namespace LabelsMis.Domain.ValueObjects;

/// <summary>
/// The vendor side of an outsourced item — who is making it, their quote reference, when it is
/// expected back, and internal-only notes. Shared by estimate lines/charges (quoting) and
/// <see cref="Entities.OutsourcedItem"/> (order tracking). Cost is deliberately not part of this
/// record: on estimate lines it is quoted per quantity break, elsewhere it sits beside the details.
/// </summary>
public record OutsourceDetails(
    Guid? VendorId,
    string? QuoteNumber,
    DateOnly? ExpectedIn,
    string? PrivateNotes)
{
    /// <summary>Trims text and turns blanks into nulls so equality and persistence are stable.</summary>
    public OutsourceDetails Normalize() => new(
        VendorId == Guid.Empty ? null : VendorId,
        string.IsNullOrWhiteSpace(QuoteNumber) ? null : QuoteNumber.Trim(),
        ExpectedIn,
        string.IsNullOrWhiteSpace(PrivateNotes) ? null : PrivateNotes.Trim());
}
