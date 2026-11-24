using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Carriers;

/// <summary>The carrier account's API keys, in the clear, for the length of one call.</summary>
public sealed record CarrierCredentials(CarrierEnvironment Environment, string ClientId, string ClientSecret);

/// <summary>What a successful sign-in says about the account behind the keys.</summary>
public sealed record CarrierSignIn(string? Account, string? Plan, DateTime ExpiresAtUtc);

/// <summary>One end of a shipment. The carrier needs a company name and a street to rate, and a
/// phone and a contact to print a label.</summary>
public sealed record CarrierParty(
    string? Company,
    string? Contact,
    string? Phone,
    string? Street1,
    string? Street2,
    string? City,
    string? State,
    string? PostalCode,
    string CountryCode = "US");

/// <summary>One box. A dimension of zero means "not measured": the carrier then bills the actual
/// weight.</summary>
public sealed record CarrierBox(decimal WeightLb, decimal LengthIn, decimal WidthIn, decimal HeightIn, decimal DeclaredValue)
{
    public bool HasDimensions => LengthIn > 0 && WidthIn > 0 && HeightIn > 0;
}

public sealed record CarrierRateRequest(
    CarrierParty ShipFrom,
    CarrierParty ShipTo,
    bool Residential,
    DateOnly ShipDate,
    IReadOnlyList<CarrierBox> Boxes,
    string? Reference1,
    string? Reference2);

/// <summary>One line of a box's price: freight, fuel, a surcharge.</summary>
public sealed record CarrierCharge(string Code, string Description, decimal Amount);

/// <summary>What one box costs on one service. <see cref="BoxKey"/> is the carrier's id for the box;
/// the booking answers with the same key, which is how a tracking number finds its box.</summary>
public sealed record CarrierBoxRate(string BoxKey, decimal Cost, IReadOnlyList<CarrierCharge> Charges);

/// <summary>One service the carrier offers for the request. <see cref="Boxes"/> is in the order
/// the boxes were asked for.</summary>
public sealed record CarrierRate(
    string OfferId,
    string ServiceCode,
    string ServiceName,
    decimal TotalCost,
    int? TransitDays,
    DateOnly? DeliveryDate,
    string? DeliveryBy,
    decimal? BilledWeightLb,
    IReadOnlyList<CarrierBoxRate> Boxes);

/// <summary>The carrier account a booking is billed to. The carrier names it in every rate.</summary>
public sealed record CarrierBillTo(string AccountNumber, string PostalCode, string CountryCode = "US");

/// <summary>The answer to a rate request. <see cref="QuoteId"/> identifies the request at the
/// carrier from here on: the booking, the labels and the void all go by it.</summary>
public sealed record CarrierRateQuote(
    string QuoteId,
    IReadOnlyList<CarrierRate> Rates,
    CarrierBillTo? BillTo,
    string? CorrelationId);

public sealed record CarrierBookRequest(string QuoteId, string OfferId, CarrierBillTo BillTo);

public sealed record CarrierBookedBox(string BoxKey, string TrackingNumber, string CarrierPackageId);

/// <summary>A bought shipment: the carrier's order number and one tracking number per box.</summary>
public sealed record CarrierBooking(string OrderNumber, IReadOnlyList<CarrierBookedBox> Boxes, string? CorrelationId);

public sealed record CarrierLabel(byte[] Content, CarrierLabelFormat Format)
{
    public string ContentType => Format == CarrierLabelFormat.Zpl ? "text/plain" : "application/pdf";
    public string FileExtension => Format == CarrierLabelFormat.Zpl ? "zpl" : "pdf";
}

public enum CarrierVoidOutcome
{
    Voided,

    /// <summary>The carrier had already voided it (in its own portal, or on an earlier try).</summary>
    AlreadyVoided
}

public sealed record CarrierTrackingEvent(string Code, DateTime OccurredAtUtc)
{
    public CarrierTrackingStatus Status => CarrierTrackingCodes.Parse(Code);
}

/// <summary>Where one box stands. <see cref="Events"/> are oldest first.</summary>
public sealed record CarrierBoxTracking(string CarrierPackageId, string StatusCode, IReadOnlyList<CarrierTrackingEvent> Events)
{
    public CarrierTrackingStatus Status => CarrierTrackingCodes.Parse(StatusCode);
}

/// <summary>The carrier's view of a shipment, every box of it. <see cref="Found"/> is false when
/// the carrier does not know the tracking number.</summary>
public sealed record CarrierTracking(bool Found, IReadOnlyList<CarrierBoxTracking> Boxes, string? CorrelationId)
{
    public static CarrierTracking NotFound(string? correlationId) => new(false, [], correlationId);
}

/// <summary>Rates, labels, voids and tracking for the shop's carrier account. One implementation
/// per carrier API; nothing above this interface knows which one it is talking to. Every failure
/// is a <see cref="CarrierException"/>.</summary>
public interface ICarrierClient
{
    /// <summary>Proves the keys: asks for a token and says whose it is.</summary>
    Task<CarrierSignIn> SignInAsync(CarrierCredentials credentials, CancellationToken cancellationToken = default);

    /// <summary>Prices the shipment on every service. Buys nothing.</summary>
    Task<CarrierRateQuote> GetRatesAsync(
        CarrierCredentials credentials, CarrierRateRequest request, CancellationToken cancellationToken = default);

    /// <summary>Buys the labels for one rate of a quote. A quote can be booked once. When this
    /// throws with <see cref="CarrierException.OutcomeUnknown"/> the carrier may have booked it
    /// anyway: repeat the call, and <see cref="CarrierErrorKind.AlreadyBooked"/> means it had.</summary>
    Task<CarrierBooking> BookAsync(
        CarrierCredentials credentials, CarrierBookRequest request, CancellationToken cancellationToken = default);

    /// <summary>One box's label (<paramref name="trackingNumber"/> required), or every box of the
    /// shipment on Letter pages (<see cref="CarrierLabelFormat.PdfLetter"/>, no tracking number).</summary>
    Task<CarrierLabel> GetLabelAsync(
        CarrierCredentials credentials,
        string quoteId,
        string? trackingNumber,
        CarrierLabelFormat format,
        CancellationToken cancellationToken = default);

    /// <summary>Voids every box of a booked shipment.</summary>
    Task<CarrierVoidOutcome> VoidAsync(
        CarrierCredentials credentials, string quoteId, CancellationToken cancellationToken = default);

    /// <summary>Every box of the shipment a tracking number belongs to.</summary>
    Task<CarrierTracking> TrackAsync(
        CarrierCredentials credentials, string trackingNumber, CancellationToken cancellationToken = default);
}

public enum CarrierErrorKind
{
    /// <summary>The carrier understood the request and said no (a rule or a bad field).</summary>
    Refused,

    /// <summary>The client ID and secret, or the token made from them, were refused.</summary>
    Unauthorized,

    /// <summary>The quote already has a booking: an earlier attempt went through.</summary>
    AlreadyBooked,

    /// <summary>No answer at all: timeout, DNS, connection reset.</summary>
    NoAnswer,

    /// <summary>5xx: the carrier itself had a problem.</summary>
    Server,

    /// <summary>An answer that could not be read, or that is missing what it must carry.</summary>
    Unreadable
}

public class CarrierException : Exception
{
    public CarrierException(
        CarrierErrorKind kind, string message, int? statusCode = null, string? correlationId = null, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        StatusCode = statusCode;
        CorrelationId = correlationId;
    }

    public CarrierErrorKind Kind { get; }
    public int? StatusCode { get; }

    /// <summary>The carrier's id for the call; its support asks for it.</summary>
    public string? CorrelationId { get; }

    /// <summary>For a call that buys or voids: the carrier may have acted even though no usable
    /// answer came back. The caller must find out before telling anyone it failed.</summary>
    public bool OutcomeUnknown =>
        Kind is CarrierErrorKind.NoAnswer or CarrierErrorKind.Server or CarrierErrorKind.Unreadable;
}

/// <summary>No carrier keys are saved (or they can't be read), so nothing can be asked of it.</summary>
public class CarrierNotConfiguredException(string message) : InvalidOperationException(message);
