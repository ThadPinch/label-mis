namespace LabelsMis.Domain.Fedex;

public record FedexAddress(
    string Street1,
    string? Street2,
    string City,
    string State,
    string Zip,
    string Country);

public record FedexRateRequest(
    FedexAddress ShipFrom,
    FedexAddress ShipTo,
    decimal WeightLb,
    decimal LengthIn,
    decimal WidthIn,
    decimal HeightIn,
    decimal DeclaredValue);

public record FedexRateOption(
    string ServiceLevel,
    string ServiceName,
    decimal Amount,
    string Currency);

public record FedexShipmentRequest(
    FedexRateRequest Package,
    string ServiceLevel,
    string ReferenceNumber);

public record FedexShipmentResult(
    string TrackingNumber,
    string LabelPath,
    decimal ShippingCost,
    string LabelFormat);

public record FedexTrackingEvent(
    DateTime EventAt,
    string StatusDescription,
    string? Location);

public interface IFedexClient
{
    Task<IReadOnlyList<FedexRateOption>> GetRateAsync(FedexRateRequest request, CancellationToken cancellationToken = default);
    Task<FedexShipmentResult> CreateShipmentAsync(FedexShipmentRequest request, CancellationToken cancellationToken = default);
    Task CancelShipmentAsync(string trackingNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FedexTrackingEvent>> GetTrackingAsync(string trackingNumber, CancellationToken cancellationToken = default);
}
