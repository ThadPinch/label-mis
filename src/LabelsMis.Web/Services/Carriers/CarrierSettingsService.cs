using LabelsMis.Domain.Carriers;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Security;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.Carriers;

/// <summary>What Setup → Shipping carrier and Record shipment show about the carrier account.
/// Carries no secret.</summary>
public sealed record CarrierSettingsView(
    bool HasCredentials,
    string? KeyProblem,
    CarrierEnvironment Environment,
    string ClientId,
    string ClientSecretTail,
    string? ShipFromContact,
    string? ShipFromPhone,
    string DefaultServiceCode,
    bool TrackingEnabled,
    IReadOnlyList<CarrierBoxPreset> BoxPresets,
    DateTime? LastTestedAt,
    bool? LastTestSucceeded,
    string? LastTestMessage,
    string? BillToAccountNumber,
    string? BillToPostalCode,
    CarrierParty ShipFrom)
{
    /// <summary>False until the deployment has a Secrets:Key; the client secret can't be saved or
    /// read without one.</summary>
    public bool CanStoreSecrets => KeyProblem is null;

    /// <summary>Keys are saved and readable: rates and labels can be asked for.</summary>
    public bool IsReady => HasCredentials && CanStoreSecrets;

    /// <summary>Labels bought now are samples: they can't ship and nothing is billed.</summary>
    public bool IsStaging => IsReady && Environment == CarrierEnvironment.Staging;

    /// <summary>The label file kept at purchase and offered first on the shipment page.</summary>
    public CarrierLabelFormat DefaultLabelFormat { get; init; } = CarrierLabelFormat.Pdf4x6;

    public static CarrierSettingsView Empty(string? keyProblem, CarrierParty shipFrom) => new(
        false, keyProblem, CarrierEnvironment.Staging, string.Empty, string.Empty, null, null,
        UpsServices.Ground, true, [], null, null, null, null, null, shipFrom);
}

/// <summary>Owns the single <see cref="CarrierSettings"/> row: the carrier API keys (the secret
/// encrypted with the app's <see cref="ISecretProtector"/>), the ship-from contact and the
/// defaults. Hands the keys, in the clear, to the one place that calls the carrier.</summary>
public class CarrierSettingsService(
    LabelsMisDbContext db,
    ICurrentUserService currentUser,
    ISecretProtector protector,
    TimeProvider time)
{
    public const string NotConfiguredMessage =
        "No carrier keys are saved. An admin adds them in Setup → Shipping carrier.";

    public async Task<CarrierSettingsView> GetViewAsync(CancellationToken cancellationToken = default)
    {
        var keyProblem = protector.IsConfigured ? null : protector.ConfigurationProblem ?? "Secrets:Key is not set.";
        var settings = await db.CarrierSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var shipFrom = await GetShipFromAsync(settings, cancellationToken);
        if (settings is null)
        {
            return CarrierSettingsView.Empty(keyProblem, shipFrom);
        }

        return new CarrierSettingsView(
            settings.HasCredentials,
            keyProblem,
            settings.Environment,
            settings.ClientId,
            settings.ClientSecretTail,
            settings.ShipFromContact,
            settings.ShipFromPhone,
            settings.DefaultServiceCode,
            settings.TrackingEnabled,
            settings.BoxPresets,
            settings.LastTestedAt,
            settings.LastTestSucceeded,
            settings.LastTestMessage,
            settings.BillToAccountNumber,
            settings.BillToPostalCode,
            shipFrom)
        {
            DefaultLabelFormat = settings.DefaultLabelFormat
        };
    }

    /// <summary>The keys in the clear, for one call. Throws <see cref="CarrierNotConfiguredException"/>
    /// when none are saved or the saved secret can't be read with the current Secrets:Key.</summary>
    public async Task<CarrierCredentials> GetCredentialsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await db.CarrierSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (settings is null || !settings.HasCredentials)
        {
            throw new CarrierNotConfiguredException(NotConfiguredMessage);
        }

        try
        {
            return new CarrierCredentials(
                settings.Environment, settings.ClientId, protector.Unprotect(settings.ClientSecretProtected));
        }
        catch (SecretProtectionException ex)
        {
            throw new CarrierNotConfiguredException(
                $"The saved carrier secret can't be read ({ex.Message}) Save it again in Setup → Shipping carrier.");
        }
    }

    /// <summary>Whether background work should ask the carrier for tracking at all.</summary>
    public Task<bool> TrackingEnabledAsync(CancellationToken cancellationToken = default) =>
        db.CarrierSettings.AsNoTracking()
            .AnyAsync(s => s.TrackingEnabled && s.ClientId != "" && s.ClientSecretProtected != "", cancellationToken);

    /// <summary>Saves the API keys; a blank secret keeps the one on file.</summary>
    public async Task SaveCredentialsAsync(
        CarrierEnvironment environment,
        string clientId,
        string? clientSecret,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUser();
        var now = Now();
        clientSecret = string.IsNullOrWhiteSpace(clientSecret) ? null : clientSecret.Trim();
        var secretProtected = clientSecret is null ? null : protector.Protect(clientSecret);
        var secretTail = clientSecret is null ? null : Tail(clientSecret);

        var settings = await GetOrCreateAsync(userId, now, cancellationToken);
        settings.SetCredentials(environment, clientId, secretProtected, secretTail, userId, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveCredentialsAsync(CancellationToken cancellationToken = default)
    {
        var userId = RequireUser();
        var settings = await db.CarrierSettings.SingleOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            return;
        }

        settings.ClearCredentials(userId, Now());
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveShipFromAsync(string? contact, string? phone, CancellationToken cancellationToken = default)
    {
        var userId = RequireUser();
        var now = Now();
        var settings = await GetOrCreateAsync(userId, now, cancellationToken);
        settings.SetShipFrom(contact, phone, userId, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveDefaultsAsync(
        string defaultServiceCode,
        bool trackingEnabled,
        IReadOnlyList<CarrierBoxPreset> boxPresets,
        CarrierLabelFormat? labelFormat = null,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUser();
        var now = Now();
        var settings = await GetOrCreateAsync(userId, now, cancellationToken);
        settings.SetDefaults(defaultServiceCode, trackingEnabled, boxPresets, userId, now, labelFormat);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Keeps the outcome of Test connection, and the account the carrier said it bills.</summary>
    public async Task RecordTestAsync(
        bool succeeded, string message, CarrierBillTo? billTo, CancellationToken cancellationToken = default)
    {
        var settings = await db.CarrierSettings.SingleOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            return;
        }

        settings.RecordTest(succeeded, message, Now());
        settings.RecordBillTo(billTo);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The shop as the carrier sees it: the company address from Settings → General, with
    /// the contact and phone from the carrier settings (the company phone when none is set).</summary>
    private async Task<CarrierParty> GetShipFromAsync(CarrierSettings? settings, CancellationToken cancellationToken)
    {
        var company = await db.GeneralSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return new CarrierParty(
            company?.CompanyName,
            settings?.ShipFromContact,
            string.IsNullOrWhiteSpace(settings?.ShipFromPhone) ? company?.Phone : settings!.ShipFromPhone,
            company?.AddressLine1,
            company?.AddressLine2,
            company?.City,
            company?.State,
            company?.Zip);
    }

    private async Task<CarrierSettings> GetOrCreateAsync(Guid userId, DateTime now, CancellationToken cancellationToken)
    {
        var settings = await db.CarrierSettings.SingleOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = CarrierSettings.Create(userId, now);
            db.CarrierSettings.Add(settings);
        }

        return settings;
    }

    private static string Tail(string secret) => secret.Length <= 4 ? new string('•', secret.Length) : secret[^4..];

    private DateTime Now() => time.GetUtcNow().UtcDateTime;

    private Guid RequireUser() =>
        currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
}
