using System.Text;
using LabelsMis.Domain.Fedex;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LabelsMis.Infrastructure.Fedex;

public class SandboxFedexClient(IOptions<FedexOptions> options, ILogger<SandboxFedexClient> logger) : IFedexClient
{
    private static readonly IReadOnlyList<FedexRateOption> RateOptions =
    [
        new("FEDEX_GROUND", "FedEx Ground", 12.50m, "USD"),
        new("FEDEX_EXPRESS_SAVER", "FedEx Express Saver", 18.75m, "USD"),
        new("FEDEX_2_DAY", "FedEx 2Day", 24.99m, "USD"),
        new("FEDEX_OVERNIGHT", "FedEx Standard Overnight", 42.00m, "USD")
    ];

    private readonly FedexOptions _options = options.Value;

    public Task<IReadOnlyList<FedexRateOption>> GetRateAsync(
        FedexRateRequest request,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug(
            "Sandbox FedEx rate quote for {WeightLb} lb to {Zip}",
            request.WeightLb,
            request.ShipTo.Zip);

        var multiplier = 1m + Math.Min(request.WeightLb / 50m, 2m);
        var rates = RateOptions
            .Select(r => r with { Amount = Math.Round(r.Amount * multiplier, 2, MidpointRounding.AwayFromZero) })
            .ToList();

        return Task.FromResult<IReadOnlyList<FedexRateOption>>(rates);
    }

    public async Task<FedexShipmentResult> CreateShipmentAsync(
        FedexShipmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var trackingNumber = GenerateTrackingNumber();
        Directory.CreateDirectory(_options.LabelStoragePath);

        var labelPath = Path.Combine(_options.LabelStoragePath, $"{trackingNumber}.pdf");
        await WriteMockLabelPdfAsync(labelPath, trackingNumber, request, cancellationToken);

        var rate = RateOptions.FirstOrDefault(r => r.ServiceLevel == request.ServiceLevel)
            ?? RateOptions[0];
        var cost = Math.Round(rate.Amount * (1m + request.Package.WeightLb / 100m), 2, MidpointRounding.AwayFromZero);

        logger.LogInformation(
            "Sandbox FedEx label created: {TrackingNumber} -> {LabelPath}",
            trackingNumber,
            labelPath);

        return new FedexShipmentResult(trackingNumber, labelPath, cost, "PDF");
    }

    public Task CancelShipmentAsync(string trackingNumber, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Sandbox FedEx cancelled shipment {TrackingNumber}", trackingNumber);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FedexTrackingEvent>> GetTrackingAsync(
        string trackingNumber,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var events = new List<FedexTrackingEvent>
        {
            new(now.AddHours(-24), "Label created", "Origin facility"),
            new(now.AddHours(-12), "In transit", "Memphis, TN"),
            new(now.AddHours(-2), "On vehicle for delivery", "Destination city")
        };

        if (trackingNumber.EndsWith('7'))
        {
            events.Add(new(now, "Delivered", "Destination city"));
        }

        return Task.FromResult<IReadOnlyList<FedexTrackingEvent>>(events);
    }

    private static string GenerateTrackingNumber() =>
        $"7489{Random.Shared.Next(100000000, 999999999)}";

    private static async Task WriteMockLabelPdfAsync(
        string labelPath,
        string trackingNumber,
        FedexShipmentRequest request,
        CancellationToken cancellationToken)
    {
        var content = $"""
            %PDF-1.4
            % Mock FedEx label — sandbox only
            Tracking: {trackingNumber}
            Reference: {request.ReferenceNumber}
            Service: {request.ServiceLevel}
            Ship To: {request.Package.ShipTo.Street1}, {request.Package.ShipTo.City}, {request.Package.ShipTo.State} {request.Package.ShipTo.Zip}
            Weight: {request.Package.WeightLb} lb
            """;

        await File.WriteAllTextAsync(labelPath, content, Encoding.UTF8, cancellationToken);
    }
}
