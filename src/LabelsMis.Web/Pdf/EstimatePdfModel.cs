namespace LabelsMis.Web.Pdf;

/// <summary>
/// Flat, EF-free view of everything the estimate PDF renders, so it can be built
/// from a saved estimate or from unsaved form state (live preview).
/// </summary>
public record EstimatePdfModel(
    string EstimateNumber,
    int RevisionNumber,
    DateTime CreatedAt,
    DateOnly? ValidUntilDate,
    string CustomerName,
    EstimatePdfAddress? BillingAddress,
    EstimatePdfAddress? ShipToAddress,
    IReadOnlyList<EstimatePdfLine> Lines,
    string? ShippingMethodName,
    decimal ShippingCost,
    bool HasShipping,
    string? Notes,
    string? SalesRepName,
    IReadOnlyList<EstimatePdfCharge>? Charges = null);

public record EstimatePdfAddress(
    string? RecipientName,
    string? Street1,
    string? Street2,
    string? City,
    string? State,
    string? Zip);

public record EstimatePdfLine(
    int LineNumber,
    string ProductDescription,
    decimal LabelAcrossIn,
    decimal LabelAroundIn,
    string SubstrateDescription,
    string InkSet,
    string? LineNotes,
    IReadOnlyList<EstimatePdfBreak> Breaks);

public record EstimatePdfBreak(int Quantity, decimal UnitPrice, decimal TotalPrice);

/// <summary>A flat, non-label charge (die creation, design time) shown once below the lines.</summary>
public record EstimatePdfCharge(string Description, int Quantity, decimal UnitPrice, decimal Total);
