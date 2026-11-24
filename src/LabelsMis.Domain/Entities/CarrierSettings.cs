using LabelsMis.Domain.Carriers;
using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Entities;

/// <summary>The shop's carrier account (single row): the Worldwide Express API keys, who the
/// carrier should call at the ship-from address, and the defaults Record shipment starts from. The
/// client secret is stored already encrypted; the entity never sees it in the clear. With no keys
/// saved nothing in the app talks to a carrier and shipments are recorded by hand, as before.</summary>
public class CarrierSettings : EntityBase
{
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-0000000000c1");

    public const int MaxMessageLength = 1000;
    public const int MaxBoxPresetsLength = 4000;

    private CarrierSettings()
    {
    }

    public CarrierEnvironment Environment { get; private set; }
    public string ClientId { get; private set; } = string.Empty;
    public string ClientSecretProtected { get; private set; } = string.Empty;

    /// <summary>Last four characters of the client secret, kept in the clear so the settings page
    /// can show which secret is saved.</summary>
    public string ClientSecretTail { get; private set; } = string.Empty;

    /// <summary>Who the carrier asks for at the shop. The address itself is the company address in
    /// Settings → General.</summary>
    public string? ShipFromContact { get; private set; }

    /// <summary>The shop's phone for the label; blank falls back to the one in Settings → General.</summary>
    public string? ShipFromPhone { get; private set; }

    /// <summary>The UPS service Record shipment selects when the rates come back.</summary>
    public string DefaultServiceCode { get; private set; } = UpsServices.Ground;

    /// <summary>The label file kept when a label is bought, and the one the shipment page offers
    /// first: it depends on what the shop prints labels on.</summary>
    public CarrierLabelFormat DefaultLabelFormat { get; private set; } = CarrierLabelFormat.Pdf4x6;

    /// <summary>Check tracking in the background for labels bought here.</summary>
    public bool TrackingEnabled { get; private set; } = true;

    /// <summary>The shop's usual boxes, as <see cref="CarrierBoxPresets.ToJson"/> wrote them.</summary>
    public string? BoxPresetsJson { get; private set; }

    public DateTime? LastTestedAt { get; private set; }
    public bool? LastTestSucceeded { get; private set; }
    public string? LastTestMessage { get; private set; }

    /// <summary>The UPS account and postal code the carrier billed in the last rate it returned.
    /// Shown on the settings page so the shop can see whose account a label is charged to.</summary>
    public string? BillToAccountNumber { get; private set; }
    public string? BillToPostalCode { get; private set; }

    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecretProtected);

    public IReadOnlyList<CarrierBoxPreset> BoxPresets => CarrierBoxPresets.FromJson(BoxPresetsJson);

    public static CarrierSettings Create(Guid createdById, DateTime createdAt)
    {
        var settings = new CarrierSettings { Environment = CarrierEnvironment.Staging };
        settings.SetCreated(SingletonId, createdById, createdAt);
        return settings;
    }

    /// <summary>Saves the API keys. A blank secret keeps the one on file. Changing the environment
    /// or the client ID forgets the last test and the account it named: they described other keys.</summary>
    public void SetCredentials(
        CarrierEnvironment environment,
        string clientId,
        string? clientSecretProtected,
        string? clientSecretTail,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        clientId = (clientId ?? string.Empty).Trim();
        if (clientId.Length == 0)
        {
            throw new ArgumentException("Client ID is required.");
        }

        var keepSecret = string.IsNullOrWhiteSpace(clientSecretProtected);
        if (keepSecret && string.IsNullOrWhiteSpace(ClientSecretProtected))
        {
            throw new ArgumentException("Client secret is required.");
        }

        var changed = !keepSecret
            || environment != Environment
            || !string.Equals(clientId, ClientId, StringComparison.Ordinal);
        Environment = environment;
        ClientId = clientId;
        if (!keepSecret)
        {
            ClientSecretProtected = clientSecretProtected!;
            ClientSecretTail = (clientSecretTail ?? string.Empty).Trim();
        }

        if (changed)
        {
            LastTestedAt = null;
            LastTestSucceeded = null;
            LastTestMessage = null;
            BillToAccountNumber = null;
            BillToPostalCode = null;
        }

        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>Removes the keys: the app goes back to recording shipments by hand. Shipments
    /// already bought keep their tracking numbers and label files.</summary>
    public void ClearCredentials(Guid modifiedById, DateTime modifiedAt)
    {
        ClientId = string.Empty;
        ClientSecretProtected = string.Empty;
        ClientSecretTail = string.Empty;
        LastTestedAt = null;
        LastTestSucceeded = null;
        LastTestMessage = null;
        BillToAccountNumber = null;
        BillToPostalCode = null;
        SetModified(modifiedById, modifiedAt);
    }

    public void SetShipFrom(string? contact, string? phone, Guid modifiedById, DateTime modifiedAt)
    {
        contact = Clean(contact);
        phone = Clean(phone);
        if (contact is { Length: > 60 })
        {
            throw new ArgumentException("The ship-from contact can be at most 60 characters.");
        }

        if (phone is not null && CarrierRequestRules.Digits(phone).Length < 10)
        {
            throw new ArgumentException("The ship-from phone needs at least 10 digits.");
        }

        ShipFromContact = contact;
        ShipFromPhone = phone;
        SetModified(modifiedById, modifiedAt);
    }

    public void SetDefaults(
        string defaultServiceCode,
        bool trackingEnabled,
        IReadOnlyList<CarrierBoxPreset> boxPresets,
        Guid modifiedById,
        DateTime modifiedAt,
        CarrierLabelFormat? labelFormat = null)
    {
        if (labelFormat is { } format && !Enum.IsDefined(format))
        {
            throw new ArgumentException("Pick one of the label files for the default.");
        }

        var code = (defaultServiceCode ?? string.Empty).Trim().ToUpperInvariant();
        if (!UpsServices.IsKnown(code))
        {
            throw new ArgumentException("Pick one of the UPS services for the default.");
        }

        var json = CarrierBoxPresets.ToJson(boxPresets);
        if (json is { Length: > MaxBoxPresetsLength })
        {
            throw new ArgumentException("That is too many box presets to keep.");
        }

        DefaultServiceCode = code;
        DefaultLabelFormat = labelFormat ?? DefaultLabelFormat;
        TrackingEnabled = trackingEnabled;
        BoxPresetsJson = json;
        SetModified(modifiedById, modifiedAt);
    }

    public void RecordTest(bool succeeded, string message, DateTime at)
    {
        message = (message ?? string.Empty).Trim();
        LastTestedAt = at;
        LastTestSucceeded = succeeded;
        LastTestMessage = message.Length > MaxMessageLength ? message[..MaxMessageLength] : message;
    }

    /// <summary>Remembers the account the carrier named in a rate, when it differs from the one on file.</summary>
    public bool RecordBillTo(CarrierBillTo? billTo)
    {
        if (billTo is null
            || (string.Equals(BillToAccountNumber, billTo.AccountNumber, StringComparison.Ordinal)
                && string.Equals(BillToPostalCode, billTo.PostalCode, StringComparison.Ordinal)))
        {
            return false;
        }

        BillToAccountNumber = billTo.AccountNumber;
        BillToPostalCode = billTo.PostalCode;
        return true;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
