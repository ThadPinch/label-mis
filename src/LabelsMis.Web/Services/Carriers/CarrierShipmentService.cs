using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LabelsMis.Domain.Carriers;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Security;
using LabelsMis.Domain.Storage;
using LabelsMis.Domain.ValueObjects;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Services.Shipments;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Carriers;

/// <summary>One box as Record shipment hands it over. A size of zero means "not measured".</summary>
public sealed record LabelBoxInput(decimal WeightLb, decimal LengthIn, decimal WidthIn, decimal HeightIn, decimal DeclaredValue);

/// <summary>Everything about a shipment that decides its price: where it goes, when, and the boxes.</summary>
public sealed record LabelRequestInput(
    DateOnly ShipDate,
    ShippingAddress ShipTo,
    string? ShipToContact,
    string? ShipToPhone,
    bool ShipToResidential,
    IReadOnlyList<LabelBoxInput> Boxes);

public sealed record QuotedBox(string BoxKey, decimal Cost);

public sealed record QuotedCharge(string Code, string Name, decimal Amount);

/// <summary>One UPS service of a quote, as Record shipment lists it.</summary>
public sealed record QuotedRate(
    string OfferId,
    string ServiceCode,
    string ServiceName,
    decimal Cost,
    int? TransitDays,
    DateOnly? DeliveryDate,
    string? DeliveryBy,
    decimal? BilledWeightLb,
    IReadOnlyList<QuotedBox> Boxes,
    IReadOnlyList<QuotedCharge> Charges);

/// <summary>The carrier's rates for a shipment. <see cref="Token"/> is the same quote sealed with
/// the app's secret key: the page carries it back, and Buy label trusts it (and nothing else the
/// browser sends) for the price.</summary>
public sealed record LabelQuote(
    string QuoteId,
    CarrierEnvironment Environment,
    DateTime FetchedAtUtc,
    CarrierBillTo BillTo,
    IReadOnlyList<QuotedRate> Rates,
    string Token);

/// <summary>The shipment as entered can't be sent to the carrier. Each problem is one the
/// shipping desk can fix on the page.</summary>
public sealed class LabelRequestException(IReadOnlyList<string> problems)
    : InvalidOperationException(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>The rates on screen can't be bought from any more; they have to be fetched again.</summary>
public sealed class StaleQuoteException(string message) : InvalidOperationException(message);

/// <summary><see cref="Confirmed"/> false means the carrier never answered: the shipment is kept
/// as pending and has to be settled with Check with carrier.</summary>
public sealed record BuyLabelResult(Guid ShipmentId, string ShipmentNumber, bool Confirmed, string Message);

public enum LabelCheckOutcome
{
    /// <summary>The label is bought and the shipment is recorded.</summary>
    Bought,

    /// <summary>There is no label (never bought, or voided unseen) and the shipment is removed.</summary>
    Discarded,

    /// <summary>Still unknown; nothing changed.</summary>
    StillOpen
}

public sealed record LabelCheckResult(LabelCheckOutcome Outcome, string Message, Guid SalesOrderId);

public sealed record LabelFile(byte[] Content, string ContentType, string FileName);

/// <summary>What asking the carrier about one shipment's boxes brought back.
/// <see cref="VoidedAtCarrier"/>: the carrier says the label is void while the shipment still
/// counts as shipped here (it was voided in the carrier's own portal).</summary>
public sealed record TrackingRefreshResult(bool Found, bool Changed, bool Delivered, bool VoidedAtCarrier, string Message);

/// <summary>Carrier labels for shipments: rates for an order's boxes, buying the label, settling a
/// purchase the carrier never answered, and the label files. What a recorded shipment does to
/// jobs, orders and invoices stays in <see cref="ShipmentService"/>; this class adds the carrier.</summary>
public class CarrierShipmentService(
    LabelsMisDbContext db,
    ShipmentService shipments,
    CarrierSettingsService settings,
    CarrierGateway gateway,
    ISecretProtector protector,
    IFileStorageClient storage,
    TimeProvider time,
    ILogger<CarrierShipmentService> logger)
{
    /// <summary>How long rates can be bought from after they were fetched.</summary>
    public static readonly TimeSpan QuoteLifetime = TimeSpan.FromMinutes(30);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record SealedQuote(
        string QuoteId,
        CarrierEnvironment Environment,
        string Fingerprint,
        DateTime FetchedAtUtc,
        string BillToAccount,
        string BillToPostalCode,
        List<QuotedRate> Rates);

    /// <summary>Prices the order's boxes on every UPS service. Buys nothing.</summary>
    public async Task<LabelQuote> GetRatesAsync(
        Guid salesOrderId,
        LabelRequestInput input,
        CancellationToken cancellationToken = default)
    {
        var (view, request) = await PrepareAsync(salesOrderId, input, cancellationToken);
        var quote = await gateway.GetRatesAsync(request, gateway.CallerFor(salesOrderId: salesOrderId), cancellationToken);
        if (quote.BillTo is null)
        {
            throw new CarrierException(
                CarrierErrorKind.Unreadable,
                "Worldwide Express did not name the UPS account to bill, so a label can't be bought from these rates.",
                correlationId: quote.CorrelationId);
        }

        var fetchedAt = Now();
        var rates = quote.Rates.Select(ToQuotedRate).ToList();
        var token = protector.Protect(JsonSerializer.Serialize(
            new SealedQuote(
                quote.QuoteId,
                view.Environment,
                Fingerprint(salesOrderId, request),
                fetchedAt,
                quote.BillTo.AccountNumber,
                quote.BillTo.PostalCode,
                rates),
            Json));
        return new LabelQuote(quote.QuoteId, view.Environment, fetchedAt, quote.BillTo, rates, token);
    }

    /// <summary>The rates a page is carrying, for showing them again; null when they can no longer
    /// be read or are too old to buy from.</summary>
    public LabelQuote? TryReadQuote(string? token)
    {
        var quote = Unseal(token);
        if (quote is null || Now() - quote.FetchedAtUtc > QuoteLifetime)
        {
            return null;
        }

        return new LabelQuote(
            quote.QuoteId,
            quote.Environment,
            quote.FetchedAtUtc,
            new CarrierBillTo(quote.BillToAccount, quote.BillToPostalCode),
            quote.Rates,
            token!);
    }

    /// <summary>
    /// Buys the label for one service of the rates on screen and records the shipment. The
    /// shipment is saved as pending before the carrier is asked, so a purchase whose answer never
    /// arrives is not lost: it comes back with <see cref="BuyLabelResult.Confirmed"/> false. A
    /// refusal leaves nothing behind.
    /// </summary>
    public async Task<BuyLabelResult> BuyLabelAsync(
        Guid salesOrderId,
        LabelRequestInput input,
        IReadOnlyList<ManualShipmentLineInput> lines,
        string? quoteToken,
        string? offerId,
        CancellationToken cancellationToken = default)
    {
        var quote = Unseal(quoteToken)
            ?? throw new StaleQuoteException("These rates can't be read any more. Get rates again.");
        if (Now() - quote.FetchedAtUtc > QuoteLifetime)
        {
            throw new StaleQuoteException(
                $"These rates are more than {QuoteLifetime.TotalMinutes:0} minutes old. Get rates again.");
        }

        var (view, request) = await PrepareAsync(salesOrderId, input, cancellationToken);
        if (view.Environment != quote.Environment)
        {
            throw new StaleQuoteException("The carrier keys changed after these rates were fetched. Get rates again.");
        }

        if (!string.Equals(Fingerprint(salesOrderId, request), quote.Fingerprint, StringComparison.Ordinal))
        {
            throw new StaleQuoteException(
                "The address, the ship date or a package changed after these rates were fetched. Get rates again.");
        }

        var rate = quote.Rates.FirstOrDefault(r => string.Equals(r.OfferId, offerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Pick one of the services to buy.");
        if (rate.Boxes.Count != input.Boxes.Count)
        {
            throw new StaleQuoteException("These rates don't cover every package. Get rates again.");
        }

        var billTo = new CarrierBillTo(quote.BillToAccount, quote.BillToPostalCode);
        var shipTo = input.ShipTo.Normalized();
        var shipment = await shipments.StartLabelShipmentAsync(
            salesOrderId,
            new LabelShipmentInput(
                input.ShipDate,
                shipTo,
                Clean(input.ShipToContact),
                Clean(input.ShipToPhone),
                input.ShipToResidential,
                lines,
                input.Boxes.Select((box, index) => new LabelPackageInput(
                    box.WeightLb, box.LengthIn, box.WidthIn, box.HeightIn, box.DeclaredValue,
                    rate.Boxes[index].BoxKey, rate.Boxes[index].Cost)).ToList(),
                quote.Environment,
                quote.QuoteId,
                rate.OfferId,
                rate.ServiceCode,
                rate.ServiceName,
                billTo,
                rate.Cost),
            cancellationToken);

        var caller = gateway.CallerFor(salesOrderId, shipment.Id);
        var detail = $"{shipment.ShipmentNumber}: {rate.ServiceName} {CarrierGateway.Money(rate.Cost)}, {CarrierGateway.Boxes(rate.Boxes.Count)}";

        // From here on the browser going away must not stop the work: a label may be bought.
        CarrierBooking booking;
        try
        {
            booking = await gateway.BookAsync(
                new CarrierBookRequest(quote.QuoteId, rate.OfferId, billTo),
                detail,
                caller,
                quote.Environment,
                cancellationToken: CancellationToken.None);
        }
        catch (CarrierException ex) when (ex.OutcomeUnknown || ex.Kind == CarrierErrorKind.AlreadyBooked)
        {
            return new BuyLabelResult(
                shipment.Id,
                shipment.ShipmentNumber,
                false,
                $"{ex.Message} LabelShop doesn't know whether the label was bought, so nothing is recorded as shipped yet. Use Check with carrier below to settle it.");
        }
        catch (Exception ex) when (ex is CarrierException or CarrierNotConfiguredException)
        {
            // Refused outright: nothing was bought, so nothing is kept.
            await shipments.DiscardUnconfirmedShipmentAsync(shipment.Id, CancellationToken.None);
            throw;
        }

        return await FinishPurchaseAsync(shipment.Id, shipment.ShipmentNumber, booking, "Label bought");
    }

    /// <summary>
    /// Settles a purchase the carrier never answered, by asking for the same booking again. A
    /// carrier quote is booked once, so the answer tells which it was: booked now (the first
    /// attempt never arrived: the label is bought and the shipment recorded), or already booked
    /// (the first attempt went through but its tracking numbers were lost with the answer: that
    /// order is voided, which costs nothing, and the shipment removed so it can be recorded again).
    /// </summary>
    public async Task<LabelCheckResult> CheckUnconfirmedAsync(Guid shipmentId, CancellationToken cancellationToken = default)
    {
        var shipment = await db.Shipments.AsNoTracking().SingleOrDefaultAsync(s => s.Id == shipmentId, cancellationToken)
            ?? throw new InvalidOperationException("Shipment not found.");
        if (shipment.Status is not ShipmentStatus.Pending
            || shipment.LabelState is not ShipmentLabelState.Unconfirmed
            || shipment.CarrierQuoteId is null
            || shipment.CarrierOfferId is null
            || shipment.CarrierBillTo is null
            || shipment.CarrierEnvironment is null)
        {
            throw new InvalidOperationException("This shipment has no label purchase waiting on the carrier.");
        }

        var environment = shipment.CarrierEnvironment.Value;
        var caller = gateway.CallerFor(shipment.SalesOrderId, shipment.Id);
        var detail = $"{shipment.ShipmentNumber}: {shipment.CarrierServiceName} {CarrierGateway.Money(shipment.CarrierCost ?? 0m)}";
        try
        {
            var booking = await gateway.BookAsync(
                new CarrierBookRequest(shipment.CarrierQuoteId, shipment.CarrierOfferId, shipment.CarrierBillTo),
                detail,
                caller,
                environment,
                CarrierOperations.BookCheck,
                CancellationToken.None);
            var bought = await FinishPurchaseAsync(
                shipment.Id,
                shipment.ShipmentNumber,
                booking,
                "Worldwide Express had not bought the label the first time, so it is bought now");
            return new LabelCheckResult(
                bought.Confirmed ? LabelCheckOutcome.Bought : LabelCheckOutcome.StillOpen, bought.Message, shipment.SalesOrderId);
        }
        catch (CarrierException ex) when (ex.Kind == CarrierErrorKind.AlreadyBooked)
        {
            // It went through the first time, and the answer that held the tracking numbers is gone.
            try
            {
                await gateway.VoidAsync(environment, shipment.CarrierQuoteId, caller, CancellationToken.None);
            }
            catch (Exception voidFailure) when (voidFailure is CarrierException or CarrierNotConfiguredException)
            {
                return new LabelCheckResult(
                    LabelCheckOutcome.StillOpen,
                    "Worldwide Express has an order for this shipment, but its tracking numbers were lost with the first answer, "
                    + $"and voiding it did not work: {voidFailure.Message} Check again in a few minutes.",
                    shipment.SalesOrderId);
            }

            await shipments.DiscardUnconfirmedShipmentAsync(shipment.Id, CancellationToken.None);
            return new LabelCheckResult(
                LabelCheckOutcome.Discarded,
                "The first attempt had gone through, but its answer never arrived, so the tracking numbers were lost. "
                + "LabelShop voided that order with Worldwide Express: nothing ships and nothing is billed. "
                + $"{shipment.ShipmentNumber} is removed. Record the shipment again to buy a new label.",
                shipment.SalesOrderId);
        }
        catch (CarrierException ex) when (ex.Kind == CarrierErrorKind.Refused)
        {
            await shipments.DiscardUnconfirmedShipmentAsync(shipment.Id, CancellationToken.None);
            return new LabelCheckResult(
                LabelCheckOutcome.Discarded,
                $"No label was bought: Worldwide Express has no order for this shipment and would not make one now ({ex.Message}). "
                + $"{shipment.ShipmentNumber} is removed. Record the shipment again.",
                shipment.SalesOrderId);
        }
        catch (Exception ex) when (ex is CarrierException or CarrierNotConfiguredException)
        {
            return new LabelCheckResult(
                LabelCheckOutcome.StillOpen,
                $"{ex.Message} Nothing has changed. Check again in a few minutes.",
                shipment.SalesOrderId);
        }
    }

    /// <summary>
    /// A bought label's file: one box's 4 × 6 PDF or ZPL, or every box on Letter pages. The copy
    /// LabelShop keeps is served when there is one; otherwise the carrier is asked and the answer
    /// is kept. A voided label is never handed out, although the carrier would still send it.
    /// </summary>
    public async Task<LabelFile> GetLabelAsync(
        Guid shipmentId,
        Guid? packageId,
        CarrierLabelFormat format,
        CancellationToken cancellationToken = default)
    {
        var shipment = await db.Shipments.AsNoTracking()
            .Include(s => s.Packages)
            .SingleOrDefaultAsync(s => s.Id == shipmentId, cancellationToken)
            ?? throw new InvalidOperationException("Shipment not found.");

        ShipmentPackage? package = null;
        if (format != CarrierLabelFormat.PdfLetter)
        {
            package = packageId is { } id
                ? shipment.Packages.SingleOrDefault(p => p.Id == id)
                    ?? throw new InvalidOperationException("That package is not on this shipment.")
                : shipment.Packages.Count == 1
                    ? shipment.Packages.Single()
                    : throw new InvalidOperationException("This shipment has several packages: pick the one to print.");
        }

        return await LoadLabelAsync(
            shipment, package, format, gateway.CallerFor(shipment.SalesOrderId, shipment.Id), cancellationToken);
    }

    /// <summary>
    /// Voids a recorded shipment. A label bought here is voided with the carrier first, and the
    /// shipment is only taken back once the carrier has said so: a void that was not confirmed
    /// changes nothing, and trying again is harmless (the carrier answers "already voided"). A
    /// shipment recorded by hand has nothing at the carrier and is simply taken back.
    /// </summary>
    /// <returns>What happened, for the person who pressed the button.</returns>
    public async Task<string> VoidAsync(Guid shipmentId, string? reason, CancellationToken cancellationToken = default)
    {
        var shipment = await db.Shipments.AsNoTracking().SingleOrDefaultAsync(s => s.Id == shipmentId, cancellationToken)
            ?? throw new InvalidOperationException("Shipment not found.");
        if (await shipments.GetVoidBlockerAsync(shipmentId, cancellationToken) is { } blocker)
        {
            throw new InvalidOperationException(blocker.Message);
        }

        var lead = "Shipment voided.";
        if (shipment.LabelState is ShipmentLabelState.Purchased)
        {
            if (shipment.CarrierQuoteId is null || shipment.CarrierEnvironment is null)
            {
                throw new InvalidOperationException("This label has nothing on file to void it by with the carrier.");
            }

            CarrierVoidOutcome outcome;
            try
            {
                outcome = await gateway.VoidAsync(
                    shipment.CarrierEnvironment.Value,
                    shipment.CarrierQuoteId,
                    gateway.CallerFor(shipment.SalesOrderId, shipment.Id),
                    CancellationToken.None);
            }
            catch (CarrierException ex) when (ex.OutcomeUnknown)
            {
                throw new InvalidOperationException(
                    $"{ex.Message} Worldwide Express did not confirm the void, so nothing was changed here. Try again: voiding twice does no harm.");
            }

            lead = outcome is CarrierVoidOutcome.AlreadyVoided
                ? "Worldwide Express had already voided this label."
                : "Label voided with Worldwide Express.";
        }

        var result = await shipments.VoidShipmentAsync(shipmentId, reason, CancellationToken.None);
        await DeleteStoredLabelsAsync(shipmentId);

        var message = new StringBuilder($"{lead} {result.ShipmentNumber} no longer counts as shipped.");
        if (result.JobsBackToReady.Count > 0)
        {
            message.Append($" {string.Join(", ", result.JobsBackToReady)} {(result.JobsBackToReady.Count == 1 ? "is" : "are")} back at ready to ship.");
        }

        if (result.OrderReopened)
        {
            message.Append($" {result.OrderNumber} is back in production.");
        }

        if (result.DraftInvoiceNumber is not null)
        {
            message.Append($" Draft invoice {result.DraftInvoiceNumber} now bills {CarrierGateway.Money(result.DraftInvoiceShipping ?? 0m)} shipping.");
        }

        return message.ToString();
    }

    /// <summary>A voided label must not be printed again, so the copies kept here go too.</summary>
    private async Task DeleteStoredLabelsAsync(Guid shipmentId)
    {
        try
        {
            foreach (var file in await storage.ListAsync(LabelFolder(shipmentId), CancellationToken.None))
            {
                await storage.DeleteAsync(file.Key, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Stored labels of voided shipment {Shipment} could not be removed", shipmentId);
        }
    }

    /// <summary>A label this new is not asked about in the background: the carrier's tracking runs
    /// behind its booking (a label bought seconds ago is "not found").</summary>
    public static readonly TimeSpan TrackingSettleTime = TimeSpan.FromMinutes(30);

    /// <summary>How long the background check leaves a shipment alone after asking about it.</summary>
    public static readonly TimeSpan TrackingRecheckAfter = TimeSpan.FromHours(4);

    /// <summary>A shipment that has not been delivered this many days after its ship date is no
    /// longer asked about in the background.</summary>
    public const int TrackingGiveUpAfterDays = 30;

    /// <summary>Asks the carrier where the shipment's boxes are, now, for the person at the screen.</summary>
    public Task<TrackingRefreshResult> RefreshTrackingAsync(Guid shipmentId, CancellationToken cancellationToken = default) =>
        RefreshTrackingAsync(shipmentId, null, cancellationToken);

    /// <summary>
    /// Asks the carrier where the shipment's boxes are and keeps what is new: each box's status,
    /// each event once (the carrier's word and time identify it), and Delivered on the shipment
    /// once every box is. One call covers every box. With a <paramref name="background"/> caller
    /// the answer is only written to the carrier log when it brought news.
    /// </summary>
    public async Task<TrackingRefreshResult> RefreshTrackingAsync(
        Guid shipmentId,
        CarrierCaller? background,
        CancellationToken cancellationToken = default)
    {
        var shipment = await db.Shipments
            .Include(s => s.Packages).ThenInclude(p => p.TrackingEvents)
            .SingleOrDefaultAsync(s => s.Id == shipmentId, cancellationToken)
            ?? throw new InvalidOperationException("Shipment not found.");
        if (shipment.LabelState is not ShipmentLabelState.Purchased || shipment.CarrierEnvironment is null)
        {
            throw new InvalidOperationException(
                "Tracking is checked for labels bought through LabelShop. For this shipment, use the carrier's own tracking page.");
        }

        var packages = shipment.Packages.OrderBy(p => p.PackageNumber).ToList();
        var asked = packages.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.TrackingNumber))
            ?? throw new InvalidOperationException("This shipment has no tracking number.");
        var caller = background ?? gateway.CallerFor(shipment.SalesOrderId, shipment.Id);
        var environment = shipment.CarrierEnvironment.Value;
        var tracking = await gateway.TrackAsync(
            environment, asked.TrackingNumber!, caller, cancellationToken, logSuccess: !caller.Automatic);
        var now = Now();

        if (!tracking.Found)
        {
            // Not news and not a verdict: the check is noted so the background doesn't ask again at once.
            shipment.ApplyTracking(caller.ActorId, now);
            await db.SaveChangesAsync(cancellationToken);
            return new TrackingRefreshResult(
                false, false, false, false,
                "Worldwide Express doesn't list this label yet. Its tracking runs a little behind a new label: check again in a few minutes.");
        }

        var news = new List<string>();
        foreach (var box in tracking.Boxes)
        {
            var package = packages.FirstOrDefault(p =>
                    string.Equals(p.CarrierPackageId, box.CarrierPackageId, StringComparison.OrdinalIgnoreCase))
                ?? (packages.Count == 1 && tracking.Boxes.Count == 1 ? packages[0] : null);
            if (package is null)
            {
                continue;
            }

            foreach (var carrierEvent in box.Events)
            {
                var code = carrierEvent.Code.Trim().ToUpperInvariant();
                if (package.TrackingEvents.Any(e => e.CarrierCode == code && e.EventAt == carrierEvent.OccurredAtUtc))
                {
                    continue;
                }

                var stored = TrackingEvent.Create(
                    Guid.NewGuid(), package.Id, carrierEvent.OccurredAtUtc, CarrierTrackingCodes.Describe(carrierEvent.Code),
                    null, null, caller.ActorId, now, code);
                package.AddTrackingEvent(stored);
                db.TrackingEvents.Add(stored);
            }

            if (package.SetTrackingStatus(box.Status))
            {
                var words = CarrierTrackingCodes.Describe(box.StatusCode);
                news.Add(packages.Count == 1 ? words : $"package {package.PackageNumber} {words.ToLowerInvariant()}");
            }
        }

        var delivered = shipment.ApplyTracking(caller.ActorId, now);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException?.Message.Contains("IX_TrackingEvent_ShipmentPackageId_CarrierCode_EventAt", StringComparison.Ordinal) == true)
        {
            // Another check stored the same events a moment ago (unique index): its work stands.
            db.ChangeTracker.Clear();
            return new TrackingRefreshResult(true, false, false, false, "Tracking was checked a moment ago. Nothing new.");
        }

        var voidedAtCarrier = shipment.Status is ShipmentStatus.InTransit
            && packages.Any(p => p.TrackingStatus == CarrierTrackingStatus.Voided);
        var changed = news.Count > 0 || delivered;
        if (changed && caller.Automatic)
        {
            await gateway.NoteAsync(
                CarrierOperations.Track, caller, environment,
                $"{shipment.ShipmentNumber}: {(news.Count > 0 ? string.Join("; ", news) : "delivered")}", tracking.CorrelationId);
        }

        var message = voidedAtCarrier
            ? "Worldwide Express reports this label as voided, but the shipment still counts as shipped here. If the label was voided in SpeedShip, void it here too so the order can ship again."
            : delivered
                ? (packages.Count == 1 ? "Delivered." : "Every package is delivered.")
                : news.Count > 0
                    ? $"Tracking updated: {string.Join("; ", news)}."
                    : $"Nothing new: {string.Join("; ", packages.Select(p => packages.Count == 1 ? CarrierTrackingCodes.Describe(p.TrackingStatus ?? CarrierTrackingStatus.Unknown).ToLowerInvariant() : $"package {p.PackageNumber} {CarrierTrackingCodes.Describe(p.TrackingStatus ?? CarrierTrackingStatus.Unknown).ToLowerInvariant()}"))}.";
        return new TrackingRefreshResult(true, changed, delivered, voidedAtCarrier, message);
    }

    /// <summary>
    /// The shipments the background check should ask about now, oldest check first: real labels
    /// (bought with production keys, which are the keys saved now) that are still in transit,
    /// have had time to show up in the carrier's tracking, and were not asked about in the last
    /// few hours. Empty when tracking is switched off or no keys are saved. Staging samples never
    /// move, so they are never in it.
    /// </summary>
    public async Task<List<Guid>> GetTrackingDueAsync(int max, CancellationToken cancellationToken = default)
    {
        if (!await settings.TrackingEnabledAsync(cancellationToken))
        {
            return [];
        }

        var view = await settings.GetViewAsync(cancellationToken);
        if (!view.IsReady || view.Environment != CarrierEnvironment.Production)
        {
            return [];
        }

        var now = Now();
        var settled = now - TrackingSettleTime;
        var recheck = now - TrackingRecheckAfter;
        var oldest = DateOnly.FromDateTime(now).AddDays(-TrackingGiveUpAfterDays);
        return await db.Shipments.AsNoTracking()
            .Where(s => s.LabelState == ShipmentLabelState.Purchased
                && s.Status == ShipmentStatus.InTransit
                && s.CarrierEnvironment == CarrierEnvironment.Production
                && s.LabelPurchasedAt <= settled
                && s.ShipDate >= oldest
                && (s.TrackingCheckedAt == null || s.TrackingCheckedAt <= recheck))
            .OrderBy(s => s.TrackingCheckedAt != null)
            .ThenBy(s => s.TrackingCheckedAt)
            .ThenBy(s => s.LabelPurchasedAt)
            .Select(s => s.Id)
            .Take(Math.Clamp(max, 1, 200))
            .ToListAsync(cancellationToken);
    }

    /// <summary>The shipment the guide's walkthrough shows as "a shipment with a label": the newest one
    /// whose label is live and in transit, else the newest one a label was ever bought for (a
    /// delivered or voided one still shows the page). Null when no label has been bought.</summary>
    public async Task<Guid?> GetNewestLabelShipmentIdAsync(CancellationToken cancellationToken = default)
    {
        var found = await db.Shipments.AsNoTracking()
            .Where(s => s.LabelState == ShipmentLabelState.Purchased || s.LabelState == ShipmentLabelState.Voided)
            .OrderByDescending(s => s.LabelState == ShipmentLabelState.Purchased && s.Status == ShipmentStatus.InTransit)
            .ThenByDescending(s => s.LabelState == ShipmentLabelState.Purchased)
            .ThenByDescending(s => s.LabelPurchasedAt)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return found;
    }

    /// <summary>Notes that a shipment was asked about although the carrier refused the question,
    /// so the background check moves on to the others instead of asking about it every time.</summary>
    public async Task NoteTrackingAskedAsync(Guid shipmentId, CancellationToken cancellationToken = default)
    {
        var now = Now();
        await db.Shipments
            .Where(s => s.Id == shipmentId)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.TrackingCheckedAt, now), cancellationToken);
    }

    public static string UnconfirmedMessage(string shipmentNumber) =>
        $"Shipment {shipmentNumber} for this order is still waiting on the carrier: a label was being bought and "
        + "Worldwide Express never answered. Open it and use Check with carrier before buying another.";

    private async Task<(CarrierSettingsView View, CarrierRateRequest Request)> PrepareAsync(
        Guid salesOrderId,
        LabelRequestInput input,
        CancellationToken cancellationToken)
    {
        var view = await settings.GetViewAsync(cancellationToken);
        if (!view.IsReady)
        {
            throw new CarrierNotConfiguredException(CarrierSettingsService.NotConfiguredMessage);
        }

        var order = await db.SalesOrders.AsNoTracking()
            .Where(o => o.Id == salesOrderId)
            .Select(o => new { o.OrderNumber, o.CustomerPoNumber })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Order not found.");

        if (await shipments.GetUnconfirmedShipmentAsync(salesOrderId, cancellationToken) is { } open)
        {
            throw new InvalidOperationException(UnconfirmedMessage(open.ShipmentNumber));
        }

        var shipTo = input.ShipTo.Normalized();
        var request = new CarrierRateRequest(
            view.ShipFrom,
            new CarrierParty(
                shipTo.RecipientName,
                Clean(input.ShipToContact),
                Clean(input.ShipToPhone),
                shipTo.Street1,
                shipTo.Street2,
                shipTo.City,
                shipTo.State,
                shipTo.Zip),
            input.ShipToResidential,
            input.ShipDate,
            input.Boxes.Select(b => new CarrierBox(b.WeightLb, b.LengthIn, b.WidthIn, b.HeightIn, b.DeclaredValue)).ToList(),
            CarrierRequestRules.Reference(order.OrderNumber),
            CarrierRequestRules.Reference(order.CustomerPoNumber));

        var problems = new List<string>(CarrierRequestRules.LabelProblems(request));
        if (shipTo.Country is { } country
            && !string.Equals(country, "US", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(country, "USA", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("LabelShop buys UPS labels for addresses in the United States only.");
        }

        var today = DateOnly.FromDateTime(Now());
        if (input.ShipDate < today.AddDays(-1) || input.ShipDate > today.AddDays(14))
        {
            problems.Add("The ship date has to be today or within the next two weeks to buy a label.");
        }

        if (problems.Count > 0)
        {
            throw new LabelRequestException(problems);
        }

        return (view, request);
    }

    /// <summary>Records a booking the carrier made, then keeps the label files.</summary>
    private async Task<BuyLabelResult> FinishPurchaseAsync(
        Guid shipmentId,
        string shipmentNumber,
        CarrierBooking booking,
        string lead)
    {
        Shipment shipment;
        try
        {
            shipment = await shipments.CompleteLabelShipmentAsync(shipmentId, booking, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex, "Label for {Shipment} was bought (carrier order {Order}) but could not be recorded",
                shipmentNumber, booking.OrderNumber);
            var state = await db.Shipments.AsNoTracking()
                .Where(s => s.Id == shipmentId)
                .Select(s => (ShipmentLabelState?)s.LabelState)
                .SingleOrDefaultAsync(CancellationToken.None);
            return state is ShipmentLabelState.Purchased
                ? new BuyLabelResult(
                    shipmentId, shipmentNumber, true,
                    $"{lead}, but the order or its invoice could not be brought up to date: {ex.Message}")
                : new BuyLabelResult(
                    shipmentId, shipmentNumber, false,
                    $"Worldwide Express bought the label (order {booking.OrderNumber}), but LabelShop could not record it: {ex.Message} "
                    + "Use Check with carrier below to settle it.");
        }

        await StoreDefaultLabelsAsync(shipment);

        var tracking = string.Join(", ", shipment.Packages.OrderBy(p => p.PackageNumber).Select(p => p.TrackingNumber));
        var billed = shipment.BilledShipping is { } amount ? $", billed shipping {CarrierGateway.Money(amount)}" : string.Empty;
        return new BuyLabelResult(
            shipment.Id,
            shipment.ShipmentNumber,
            true,
            $"{lead}: {shipment.CarrierServiceName}, tracking {tracking}. "
            + $"Carrier cost {CarrierGateway.Money(shipment.CarrierCost ?? 0m)}{billed}."
            + (shipment.IsSampleLabel ? " This is a staging sample: it can't ship and nothing is billed." : string.Empty));
    }

    /// <summary>Keeps the label files in the shop's format right after the purchase. A failure here
    /// does not undo the purchase: the files are fetched when they are printed.</summary>
    private async Task StoreDefaultLabelsAsync(Shipment shipment)
    {
        try
        {
            var view = await settings.GetViewAsync(CancellationToken.None);
            var caller = gateway.CallerFor(shipment.SalesOrderId, shipment.Id);
            if (view.DefaultLabelFormat == CarrierLabelFormat.PdfLetter)
            {
                await LoadLabelAsync(shipment, null, CarrierLabelFormat.PdfLetter, caller, CancellationToken.None);
                return;
            }

            foreach (var package in shipment.Packages.OrderBy(p => p.PackageNumber))
            {
                await LoadLabelAsync(shipment, package, view.DefaultLabelFormat, caller, CancellationToken.None);
            }
        }
        catch (Exception ex) when (ex is CarrierException or CarrierNotConfiguredException or InvalidOperationException)
        {
            logger.LogWarning(
                ex, "Labels for {Shipment} were bought but are not stored yet; they are fetched when printed",
                shipment.ShipmentNumber);
        }
    }

    private async Task<LabelFile> LoadLabelAsync(
        Shipment shipment,
        ShipmentPackage? package,
        CarrierLabelFormat format,
        CarrierCaller caller,
        CancellationToken cancellationToken)
    {
        if (shipment.LabelState is not ShipmentLabelState.Purchased
            || shipment.CarrierQuoteId is null
            || shipment.CarrierEnvironment is null)
        {
            throw new InvalidOperationException(shipment.LabelState switch
            {
                ShipmentLabelState.Voided => "This label was voided, so it can't be printed.",
                ShipmentLabelState.Unconfirmed => "The label purchase hasn't been confirmed by the carrier yet.",
                _ => "No label was bought through LabelShop for this shipment."
            });
        }

        var tracking = package?.TrackingNumber;
        if (format != CarrierLabelFormat.PdfLetter && string.IsNullOrWhiteSpace(tracking))
        {
            throw new InvalidOperationException("That package has no tracking number.");
        }

        var name = format switch
        {
            CarrierLabelFormat.PdfLetter => $"{shipment.ShipmentNumber}-labels-letter.pdf",
            CarrierLabelFormat.Zpl => $"{shipment.ShipmentNumber}-{tracking}.zpl",
            _ => $"{shipment.ShipmentNumber}-{tracking}-4x6.pdf"
        };
        var contentType = format == CarrierLabelFormat.Zpl ? "text/plain" : "application/pdf";
        var key = LabelKey(shipment.Id, name);

        try
        {
            if (await storage.ExistsAsync(key, cancellationToken))
            {
                await using var stored = await storage.OpenReadAsync(key, cancellationToken);
                using var buffer = new MemoryStream();
                await stored.CopyToAsync(buffer, cancellationToken);
                if (buffer.Length > 0)
                {
                    return new LabelFile(buffer.ToArray(), contentType, name);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Stored label {Key} could not be read; asking the carrier for it", key);
        }

        var label = await gateway.GetLabelAsync(
            shipment.CarrierEnvironment.Value, shipment.CarrierQuoteId, tracking, format, caller, cancellationToken);
        try
        {
            using var content = new MemoryStream(label.Content);
            await storage.UploadAsync(key, content, label.ContentType, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Label {Key} could not be stored; it will be asked for again next time", key);
        }

        return new LabelFile(label.Content, label.ContentType, name);
    }

    /// <summary>Where a shipment's label files are kept in file storage.</summary>
    public static string LabelFolder(Guid shipmentId) => $"carrier-labels/{shipmentId:N}/";

    private static string LabelKey(Guid shipmentId, string fileName) => LabelFolder(shipmentId) + fileName;

    private static QuotedRate ToQuotedRate(CarrierRate rate) => new(
        rate.OfferId,
        rate.ServiceCode,
        rate.ServiceName,
        rate.TotalCost,
        rate.TransitDays,
        rate.DeliveryDate,
        rate.DeliveryBy,
        rate.BilledWeightLb,
        rate.Boxes.Select(b => new QuotedBox(b.BoxKey, b.Cost)).ToList(),
        rate.Boxes.SelectMany(b => b.Charges)
            .GroupBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
            .Select(g => new QuotedCharge(g.Key, UpsCharges.Name(g.Key, g.First().Description), g.Sum(c => c.Amount)))
            .OrderByDescending(c => c.Amount)
            .ToList());

    /// <summary>Everything the carrier priced, written one way: the same shipment always gives the
    /// same text, however its numbers were typed ("5" and "5.0" are one weight).</summary>
    private static string Fingerprint(Guid salesOrderId, CarrierRateRequest request)
    {
        static string Number(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
        static string Party(CarrierParty p) => string.Join(
            '|', p.Company, p.Contact, p.Phone, p.Street1, p.Street2, p.City, p.State, p.PostalCode, p.CountryCode);

        var text = string.Join(
            '\n',
            salesOrderId.ToString("N"),
            Party(request.ShipFrom),
            Party(request.ShipTo),
            request.Residential ? "residential" : "commercial",
            request.ShipDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            request.Reference1,
            request.Reference2,
            string.Join(';', request.Boxes.Select(b =>
                $"{Number(b.WeightLb)},{Number(b.LengthIn)},{Number(b.WidthIn)},{Number(b.HeightIn)},{Number(b.DeclaredValue)}")));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private SealedQuote? Unseal(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SealedQuote>(protector.Unprotect(token), Json);
        }
        catch (Exception ex) when (ex is SecretProtectionException or JsonException or FormatException or CryptographicException)
        {
            return null;
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
