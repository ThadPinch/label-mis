namespace LabelsMis.Domain.ValueObjects;

/// <summary>
/// A point-in-time snapshot of a ship-to address. Copied onto estimates and sales orders
/// so later edits to a customer's address don't alter historical records.
/// </summary>
public record ShippingAddress(
    string? RecipientName,
    string? Street1,
    string? Street2,
    string? City,
    string? State,
    string? Zip,
    string? Country)
{
    public static readonly ShippingAddress Empty = new(null, null, null, null, null, null, null);

    public bool HasAddress => !string.IsNullOrWhiteSpace(Street1);

    public ShippingAddress Normalized() => new(
        Trim(RecipientName),
        Trim(Street1),
        Trim(Street2),
        Trim(City),
        Trim(State),
        Trim(Zip),
        Trim(Country));

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
