namespace LabelsMis.Infrastructure.Storage;

public static class SpacesEndpointNormalizer
{
    /// <summary>
    /// DigitalOcean Spaces requires us-east-1 for AWS SDK request signing regardless of datacenter.
    /// </summary>
    public const string SigningRegion = "us-east-1";

    public static string NormalizeServiceUrl(string serviceUrl, string bucketName)
    {
        if (string.IsNullOrWhiteSpace(serviceUrl))
        {
            return string.Empty;
        }

        var url = serviceUrl.Trim().TrimEnd('/');
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

    public static string? ParseRegionFromServiceUrl(string serviceUrl)
    {
        if (!Uri.TryCreate(serviceUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var parts = uri.Host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3
            && parts[^2].Equals("digitaloceanspaces", StringComparison.OrdinalIgnoreCase)
            && parts[^1].Equals("com", StringComparison.OrdinalIgnoreCase))
        {
            return parts[0];
        }

        return null;
    }
}
