using LabelsMis.Domain.Common;

namespace LabelsMis.Domain.Entities;

public class StorageSettings : EntityBase
{
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private StorageSettings()
    {
    }

    public string ServiceUrl { get; private set; } = string.Empty;
    public string BucketName { get; private set; } = string.Empty;
    public string AccessKey { get; private set; } = string.Empty;
    public string SecretKey { get; private set; } = string.Empty;
    public string? Region { get; private set; }
    public string ArtworkKeyPrefix { get; private set; } = "artwork/";
    public bool UseLocalFallback { get; private set; } = true;

    public static StorageSettings CreateDefault(Guid createdById, DateTime createdAt)
    {
        var settings = new StorageSettings
        {
            UseLocalFallback = true,
            ArtworkKeyPrefix = "artwork/"
        };
        settings.SetCreated(SingletonId, createdById, createdAt);
        return settings;
    }

    public void Update(
        string serviceUrl,
        string bucketName,
        string accessKey,
        string secretKey,
        string? region,
        string artworkKeyPrefix,
        bool useLocalFallback,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        BucketName = bucketName.Trim();
        ServiceUrl = NormalizeServiceUrl(serviceUrl, BucketName);
        AccessKey = accessKey.Trim();
        if (!string.IsNullOrWhiteSpace(secretKey))
        {
            SecretKey = secretKey.Trim();
        }

        Region = string.IsNullOrWhiteSpace(region) ? null : region.Trim();
        ArtworkKeyPrefix = string.IsNullOrWhiteSpace(artworkKeyPrefix) ? "artwork/" : artworkKeyPrefix.Trim();
        if (!ArtworkKeyPrefix.EndsWith('/'))
        {
            ArtworkKeyPrefix += "/";
        }

        UseLocalFallback = useLocalFallback;
        SetModified(modifiedById, modifiedAt);
    }

    public bool IsSpacesConfigured =>
        !UseLocalFallback
        && !string.IsNullOrWhiteSpace(ServiceUrl)
        && !string.IsNullOrWhiteSpace(BucketName)
        && !string.IsNullOrWhiteSpace(AccessKey)
        && !string.IsNullOrWhiteSpace(SecretKey);

    private static string NormalizeServiceUrl(string serviceUrl, string bucketName)
    {
        var url = serviceUrl.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        var host = uri.Host;
        if (!string.IsNullOrWhiteSpace(bucketName)
            && host.StartsWith($"{bucketName}.", StringComparison.OrdinalIgnoreCase))
        {
            host = host[(bucketName.Length + 1)..];
        }

        return $"{uri.Scheme}://{host}";
    }
}
