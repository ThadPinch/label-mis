using LabelsMis.Domain.Common;

namespace LabelsMis.Domain.Entities;

public class EmailSettings : EntityBase
{
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    public const string DefaultApiBaseUrl = "https://api.mailgun.net";

    private EmailSettings()
    {
    }

    public bool Enabled { get; private set; }
    public string ApiBaseUrl { get; private set; } = DefaultApiBaseUrl;
    public string Domain { get; private set; } = string.Empty;
    public string ApiKey { get; private set; } = string.Empty;
    public string FromName { get; private set; } = string.Empty;
    public string FromEmail { get; private set; } = string.Empty;

    public static EmailSettings CreateDefault(Guid createdById, DateTime createdAt)
    {
        var settings = new EmailSettings
        {
            Enabled = false,
            ApiBaseUrl = DefaultApiBaseUrl
        };
        settings.SetCreated(SingletonId, createdById, createdAt);
        return settings;
    }

    public void Update(
        bool enabled,
        string apiBaseUrl,
        string domain,
        string apiKey,
        string fromName,
        string fromEmail,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        Enabled = enabled;
        ApiBaseUrl = string.IsNullOrWhiteSpace(apiBaseUrl)
            ? DefaultApiBaseUrl
            : apiBaseUrl.Trim().TrimEnd('/');
        Domain = domain.Trim();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            ApiKey = apiKey.Trim();
        }

        FromName = fromName.Trim();
        FromEmail = fromEmail.Trim();
        SetModified(modifiedById, modifiedAt);
    }

    public bool IsConfigured =>
        Enabled
        && !string.IsNullOrWhiteSpace(ApiBaseUrl)
        && !string.IsNullOrWhiteSpace(Domain)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(FromEmail);

    public string FromHeader =>
        string.IsNullOrWhiteSpace(FromName) ? FromEmail : $"{FromName} <{FromEmail}>";
}
