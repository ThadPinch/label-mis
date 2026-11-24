using System.Diagnostics;
using System.Globalization;
using LabelsMis.Domain.Carriers;
using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Web.Services.Carriers;

/// <summary>Who a carrier call is made for, and which order or shipment it is about, so the log
/// can show a shipment's own history. <see cref="Automatic"/> marks background work.</summary>
public sealed record CarrierCaller(Guid ActorId, bool Automatic = false, Guid? SalesOrderId = null, Guid? ShipmentId = null)
{
    /// <summary>Background work, filed under the system user.</summary>
    public static CarrierCaller Background(Guid? shipmentId = null) =>
        new(TenantConstants.SystemUserId, Automatic: true, ShipmentId: shipmentId);
}

/// <summary>The one door every carrier call goes through. It adds what a bare client call lacks:
/// the saved keys, a check that a shipment is being handled in the world it was bought in, and a
/// row in the carrier log for every attempt, worked or not.</summary>
public class CarrierGateway(
    ICarrierClient client,
    CarrierSettingsService settings,
    CarrierLogger log,
    ICurrentUserService currentUser)
{
    /// <summary>The signed-in user as the caller, for a call about one order or one shipment.</summary>
    public CarrierCaller CallerFor(Guid? salesOrderId = null, Guid? shipmentId = null) =>
        new(currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated."), false, salesOrderId, shipmentId);

    public Task<CarrierSignIn> SignInAsync(CarrierCaller caller, CancellationToken cancellationToken = default) =>
        SendAsync(
            CarrierOperations.SignIn,
            caller,
            null,
            null,
            credentials => client.SignInAsync(credentials, cancellationToken),
            signIn => (signIn.Account is null ? "Signed in" : $"Signed in as {signIn.Account}", null),
            cancellationToken);

    public Task<CarrierRateQuote> GetRatesAsync(
        CarrierRateRequest request, CarrierCaller caller, CancellationToken cancellationToken = default) =>
        SendAsync(
            CarrierOperations.Rates,
            caller,
            null,
            $"{Boxes(request.Boxes.Count)}, {Number(request.Boxes.Sum(b => b.WeightLb))} lb to {request.ShipTo.PostalCode}",
            credentials => client.GetRatesAsync(credentials, request, cancellationToken),
            quote =>
            {
                var cheapest = quote.Rates[0];
                return ($"{quote.Rates.Count} services, from {cheapest.ServiceName} {Money(cheapest.TotalCost)}", quote.CorrelationId);
            },
            cancellationToken);

    /// <summary>Buys the labels. <paramref name="operation"/> is <see cref="CarrierOperations.BookCheck"/>
    /// when this is the repeat that settles an unanswered purchase.</summary>
    public Task<CarrierBooking> BookAsync(
        CarrierBookRequest request,
        string detail,
        CarrierCaller caller,
        CarrierEnvironment? boughtIn = null,
        string operation = CarrierOperations.Book,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            operation,
            caller,
            boughtIn,
            detail,
            credentials => client.BookAsync(credentials, request, cancellationToken),
            booking => ($"Order {booking.OrderNumber}: {string.Join(", ", booking.Boxes.Select(b => b.TrackingNumber))}", booking.CorrelationId),
            cancellationToken);

    public Task<CarrierLabel> GetLabelAsync(
        CarrierEnvironment boughtIn,
        string quoteId,
        string? trackingNumber,
        CarrierLabelFormat format,
        CarrierCaller caller,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            CarrierOperations.Label,
            caller,
            boughtIn,
            format == CarrierLabelFormat.PdfLetter ? "All boxes, Letter PDF" : $"{trackingNumber}, {(format == CarrierLabelFormat.Zpl ? "ZPL" : "4 × 6 PDF")}",
            credentials => client.GetLabelAsync(credentials, quoteId, trackingNumber, format, cancellationToken),
            label => ($"{label.Content.Length.ToString("N0", CultureInfo.InvariantCulture)} bytes", null),
            cancellationToken);

    public Task<CarrierVoidOutcome> VoidAsync(
        CarrierEnvironment boughtIn, string quoteId, CarrierCaller caller, CancellationToken cancellationToken = default) =>
        SendAsync(
            CarrierOperations.Void,
            caller,
            boughtIn,
            null,
            credentials => client.VoidAsync(credentials, quoteId, cancellationToken),
            outcome => (outcome == CarrierVoidOutcome.AlreadyVoided ? "Already voided at the carrier" : "Voided", null),
            cancellationToken);

    public Task<CarrierTracking> TrackAsync(
        CarrierEnvironment boughtIn, string trackingNumber, CarrierCaller caller, CancellationToken cancellationToken = default) =>
        SendAsync(
            CarrierOperations.Track,
            caller,
            boughtIn,
            trackingNumber,
            credentials => client.TrackAsync(credentials, trackingNumber, cancellationToken),
            tracking => (
                tracking.Found
                    ? string.Join(", ", tracking.Boxes.Select(b => CarrierTrackingCodes.Describe(b.StatusCode)))
                    : "The carrier does not know this tracking number",
                tracking.CorrelationId),
            cancellationToken);

    private async Task<T> SendAsync<T>(
        string operation,
        CarrierCaller caller,
        CarrierEnvironment? boughtIn,
        string? detail,
        Func<CarrierCredentials, Task<T>> call,
        Func<T, (string? Outcome, string? CorrelationId)> describe,
        CancellationToken cancellationToken)
    {
        var environment = boughtIn ?? CarrierEnvironment.Staging;
        var started = Stopwatch.GetTimestamp();
        try
        {
            var credentials = await settings.GetCredentialsAsync(cancellationToken);
            if (boughtIn is { } world && world != credentials.Environment)
            {
                // A staging label does not exist in production and the other way round: asking the
                // wrong world would answer "not found" for a label that is real.
                throw new CarrierNotConfiguredException(
                    $"This label was bought with the carrier's {Words(world)} keys, and the keys saved now are for {Words(credentials.Environment)}. "
                    + "It can only be handled with the keys it was bought with.");
            }

            environment = credentials.Environment;
            var result = await call(credentials);
            var (outcome, correlationId) = describe(result);
            await log.WriteAsync(new CarrierLogEntry(
                operation, true, caller.ActorId, environment, caller.Automatic, caller.SalesOrderId, caller.ShipmentId,
                Join(detail, outcome), CorrelationId: correlationId, DurationMs: Elapsed(started)));
            return result;
        }
        catch (CarrierException ex)
        {
            await log.WriteAsync(new CarrierLogEntry(
                operation, false, caller.ActorId, environment, caller.Automatic, caller.SalesOrderId, caller.ShipmentId,
                detail, ex.StatusCode, ex.Kind.ToString(), ex.Message, ex.CorrelationId, Elapsed(started)));
            throw;
        }
        catch (CarrierNotConfiguredException ex)
        {
            await log.WriteAsync(new CarrierLogEntry(
                operation, false, caller.ActorId, environment, caller.Automatic, caller.SalesOrderId, caller.ShipmentId,
                detail, ErrorKind: "NotConfigured", Message: ex.Message, DurationMs: Elapsed(started)));
            throw;
        }
    }

    public static string Words(CarrierEnvironment environment) =>
        environment == CarrierEnvironment.Production ? "production" : "staging";

    /// <summary>Money as the log and the messages write it, whatever the server's culture.</summary>
    public static string Money(decimal amount) => "$" + amount.ToString("#,##0.00", CultureInfo.InvariantCulture);

    public static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    public static string Boxes(int count) => count == 1 ? "1 box" : $"{count} boxes";

    private static string? Join(string? detail, string? outcome) =>
        (detail, outcome) switch
        {
            (null, _) => outcome,
            (_, null) => detail,
            _ => $"{detail}: {outcome}"
        };

    private static int Elapsed(long started) => (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}

public sealed record CarrierTestResult(bool Succeeded, string Message);

/// <summary>Test connection on Setup → Shipping carrier: signs in with the saved keys and, when
/// the ship-from address is complete, rates a 1 lb box from the shop to itself. That proves the
/// whole path a label purchase takes and shows which UPS account the carrier bills.</summary>
public class CarrierConnectionTest(CarrierSettingsService settings, CarrierGateway gateway, TimeProvider time)
{
    public async Task<CarrierTestResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var caller = gateway.CallerFor();
        CarrierBillTo? billTo = null;
        CarrierTestResult result;
        try
        {
            var signIn = await gateway.SignInAsync(caller, cancellationToken);
            var view = await settings.GetViewAsync(cancellationToken);
            var message = signIn.Account is null
                ? "Signed in to Worldwide Express."
                : $"Signed in to Worldwide Express as account {signIn.Account}{(signIn.Plan is null ? string.Empty : $" (plan {signIn.Plan})")}.";

            var request = new CarrierRateRequest(
                view.ShipFrom,
                view.ShipFrom,
                Residential: false,
                DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime),
                [new CarrierBox(1m, 0m, 0m, 0m, 0m)],
                "LabelShop connection test",
                null);
            var missing = CarrierRequestRules.Problems(request);
            if (missing.Count > 0)
            {
                message += " No test rate was asked for: " + string.Join(" ", missing.Distinct());
            }
            else
            {
                var quote = await gateway.GetRatesAsync(request, caller, cancellationToken);
                billTo = quote.BillTo;
                var cheapest = quote.Rates[0];
                message += $" A 1 lb test box from the ship-from address came back with {quote.Rates.Count} UPS services"
                    + $" (from {cheapest.ServiceName} at {CarrierGateway.Money(cheapest.TotalCost)})."
                    + (billTo is null ? string.Empty : $" Labels bill UPS account {billTo.AccountNumber} (ZIP {billTo.PostalCode}).");
            }

            result = new CarrierTestResult(true, message);
        }
        catch (Exception ex) when (ex is CarrierException or CarrierNotConfiguredException)
        {
            result = new CarrierTestResult(false, ex.Message);
        }

        await settings.RecordTestAsync(result.Succeeded, result.Message, billTo, cancellationToken);
        return result;
    }
}
