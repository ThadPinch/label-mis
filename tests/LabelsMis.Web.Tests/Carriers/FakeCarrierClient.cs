using LabelsMis.Domain.Carriers;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Web.Tests.Carriers;

/// <summary>Stands in for Worldwide Express. Left alone it behaves like their staging did on
/// 2026-11-24: a quote can be booked once, a booked quote can be voided, a second void says so, an
/// unknown tracking number is not found. A test that needs something else queues an answer (or a
/// throw) on the matching script; each queued entry answers one call.</summary>
public sealed class FakeCarrierClient : ICarrierClient
{
    private readonly Dictionary<string, FakeShipment> _shipments = [];
    private int _quotes;
    private int _tracking;

    public List<(string Operation, CarrierCredentials Credentials)> Calls { get; } = [];
    public List<CarrierRateRequest> RateRequests { get; } = [];
    public List<CarrierBookRequest> BookRequests { get; } = [];
    public List<(string QuoteId, string? TrackingNumber, CarrierLabelFormat Format)> LabelRequests { get; } = [];
    public List<string> VoidRequests { get; } = [];
    public List<string> TrackRequests { get; } = [];

    public Queue<Func<CarrierSignIn>> SignInScript { get; } = new();
    public Queue<Func<CarrierRateRequest, CarrierRateQuote>> RatesScript { get; } = new();
    public Queue<Func<CarrierBookRequest, CarrierBooking>> BookScript { get; } = new();
    public Queue<Func<CarrierLabel>> LabelScript { get; } = new();
    public Queue<Func<string, CarrierVoidOutcome>> VoidScript { get; } = new();
    public Queue<Func<string, CarrierTracking>> TrackScript { get; } = new();

    /// <summary>The account every rate says it bills.</summary>
    public CarrierBillTo BillTo { get; set; } = new("V01714", "75226");

    /// <summary>The status word tracking reports for a booked, unvoided box.</summary>
    public string TrackingStatus { get; set; } = "CREATED";

    public int CountOf(string operation) => Calls.Count(c => c.Operation == operation);

    /// <summary>Ground for one box of this weight, as the default rates price it.</summary>
    public static decimal GroundCost(decimal weightLb) => 10m + weightLb;

    public Task<CarrierSignIn> SignInAsync(CarrierCredentials credentials, CancellationToken cancellationToken = default)
    {
        Calls.Add((CarrierOperations.SignIn, credentials));
        return Task.FromResult(SignInScript.TryDequeue(out var scripted)
            ? scripted()
            : new CarrierSignIn("W0000000000", "small", new DateTime(2026, 11, 25, 17, 0, 0, DateTimeKind.Utc)));
    }

    public Task<CarrierRateQuote> GetRatesAsync(
        CarrierCredentials credentials, CarrierRateRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add((CarrierOperations.Rates, credentials));
        RateRequests.Add(request);
        if (RatesScript.TryDequeue(out var scripted))
        {
            return Task.FromResult(Remember(scripted(request)));
        }

        var quoteId = $"quote-{++_quotes}";
        var keys = request.Boxes.Select((_, i) => $"{quoteId}-box-{i + 1}").ToList();
        CarrierRate Rate(string code, decimal factor, int days) => new(
            $"{quoteId}-{code}",
            code,
            UpsServices.Name(code),
            request.Boxes.Sum(b => GroundCost(b.WeightLb) * factor),
            days,
            request.ShipDate.AddDays(days),
            "23:00:00",
            request.Boxes.Sum(b => b.WeightLb),
            request.Boxes.Select((b, i) => new CarrierBoxRate(
                keys[i],
                GroundCost(b.WeightLb) * factor,
                [new CarrierCharge("FRT", "SMALL PACKAGE FREIGHT", GroundCost(b.WeightLb) * factor)])).ToList());

        return Task.FromResult(Remember(new CarrierRateQuote(
            quoteId, [Rate("GND", 1m, 3), Rate("2DA", 3m, 2), Rate("1DA", 6m, 1)], BillTo, $"NG-rates-{_quotes}")));
    }

    public Task<CarrierBooking> BookAsync(
        CarrierCredentials credentials, CarrierBookRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add((CarrierOperations.Book, credentials));
        BookRequests.Add(request);
        if (BookScript.TryDequeue(out var scripted))
        {
            return Task.FromResult(scripted(request));
        }

        return Task.FromResult(Book(request));
    }

    /// <summary>What the carrier does with a booking: once per quote, for a rate of that quote.
    /// A script can call it to book and then lose the answer.</summary>
    public CarrierBooking Book(CarrierBookRequest request)
    {
        if (!_shipments.TryGetValue(request.QuoteId, out var shipment))
        {
            throw new CarrierException(
                CarrierErrorKind.Refused, "Worldwide Express: no product transaction records found", 422, "NG-book-refused");
        }

        if (shipment.Booking is not null)
        {
            throw new CarrierException(
                CarrierErrorKind.AlreadyBooked, "Worldwide Express already has an order for this rate request.", 200, "NG-book-again");
        }

        var rate = shipment.Quote.Rates.FirstOrDefault(r => r.OfferId == request.OfferId)
            ?? throw new CarrierException(
                CarrierErrorKind.Refused,
                "Worldwide Express: no offers found for the requested offer id on product transaction record",
                422,
                "NG-book-refused");

        shipment.Booking = new CarrierBooking(
            $"NUD{90000000 + _shipments.Values.Count(s => s.Booking is not null) + 1}",
            rate.Boxes.Select(b => new CarrierBookedBox(b.BoxKey, $"1ZTEST{++_tracking:D12}", $"item-{_tracking}")).ToList(),
            $"NG-book-{request.QuoteId}");
        return shipment.Booking;
    }

    public Task<CarrierLabel> GetLabelAsync(
        CarrierCredentials credentials,
        string quoteId,
        string? trackingNumber,
        CarrierLabelFormat format,
        CancellationToken cancellationToken = default)
    {
        Calls.Add((CarrierOperations.Label, credentials));
        LabelRequests.Add((quoteId, trackingNumber, format));
        if (LabelScript.TryDequeue(out var scripted))
        {
            return Task.FromResult(scripted());
        }

        var text = format == CarrierLabelFormat.Zpl
            ? $"^XA^FD{trackingNumber}^FS^XZ"
            : $"%PDF-1.4 {format} {trackingNumber ?? quoteId}";
        return Task.FromResult(new CarrierLabel(System.Text.Encoding.ASCII.GetBytes(text), format));
    }

    public Task<CarrierVoidOutcome> VoidAsync(
        CarrierCredentials credentials, string quoteId, CancellationToken cancellationToken = default)
    {
        Calls.Add((CarrierOperations.Void, credentials));
        VoidRequests.Add(quoteId);
        if (VoidScript.TryDequeue(out var scripted))
        {
            return Task.FromResult(scripted(quoteId));
        }

        return Task.FromResult(Void(quoteId));
    }

    /// <summary>What the carrier does with a void. A script can call it to void and then lose the answer.</summary>
    public CarrierVoidOutcome Void(string quoteId)
    {
        if (!_shipments.TryGetValue(quoteId, out var shipment) || shipment.Booking is null)
        {
            throw new CarrierException(
                CarrierErrorKind.Server, "Worldwide Express had a problem and could not complete the request (answer 500).", 500);
        }

        if (shipment.Voided)
        {
            return CarrierVoidOutcome.AlreadyVoided;
        }

        shipment.Voided = true;
        return CarrierVoidOutcome.Voided;
    }

    public Task<CarrierTracking> TrackAsync(
        CarrierCredentials credentials, string trackingNumber, CancellationToken cancellationToken = default)
    {
        Calls.Add((CarrierOperations.Track, credentials));
        TrackRequests.Add(trackingNumber);
        if (TrackScript.TryDequeue(out var scripted))
        {
            return Task.FromResult(scripted(trackingNumber));
        }

        var shipment = _shipments.Values.FirstOrDefault(
            s => s.Booking is not null && s.Booking.Boxes.Any(b => b.TrackingNumber == trackingNumber));
        if (shipment?.Booking is null)
        {
            return Task.FromResult(CarrierTracking.NotFound("NG-track-unknown"));
        }

        var status = shipment.Voided ? "VOID" : TrackingStatus;
        var created = new CarrierTrackingEvent("CREATED", new DateTime(2026, 11, 24, 17, 0, 0, DateTimeKind.Utc));
        IReadOnlyList<CarrierTrackingEvent> events = status == "CREATED"
            ? [created]
            : [created, new CarrierTrackingEvent(status, new DateTime(2026, 11, 25, 9, 30, 0, DateTimeKind.Utc))];
        return Task.FromResult(new CarrierTracking(
            true,
            shipment.Booking.Boxes.Select(b => new CarrierBoxTracking(b.CarrierPackageId, status, events)).ToList(),
            "NG-track"));
    }

    /// <summary>True once the quote has been booked at the fake carrier.</summary>
    public bool IsBooked(string quoteId) => _shipments.TryGetValue(quoteId, out var s) && s.Booking is not null;

    /// <summary>True once the quote's booking has been voided at the fake carrier.</summary>
    public bool IsVoided(string quoteId) => _shipments.TryGetValue(quoteId, out var s) && s.Voided;

    public CarrierBooking? BookingOf(string quoteId) => _shipments.GetValueOrDefault(quoteId)?.Booking;

    private CarrierRateQuote Remember(CarrierRateQuote quote)
    {
        _shipments[quote.QuoteId] = new FakeShipment(quote);
        return quote;
    }

    private sealed class FakeShipment(CarrierRateQuote quote)
    {
        public CarrierRateQuote Quote { get; } = quote;
        public CarrierBooking? Booking { get; set; }
        public bool Voided { get; set; }
    }
}
