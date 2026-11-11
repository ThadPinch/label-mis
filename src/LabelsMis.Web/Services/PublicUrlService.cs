using Microsoft.Extensions.Options;

namespace LabelsMis.Web.Services;

public class AppUrlOptions
{
    public const string SectionName = "App";

    /// <summary>The address people reach the app at, e.g. <c>https://mis.heyluckylabel.com</c>.
    /// Leave empty to use the address of the current request.</summary>
    public string? PublicBaseUrl { get; set; }
}

/// <summary>Builds absolute links that leave the app, such as the QuickBooks OAuth redirect URI.
/// An outside party compares these character for character, so the configured address wins over
/// whatever the current request looks like.</summary>
public class PublicUrlService(IOptions<AppUrlOptions> options, IHttpContextAccessor httpContextAccessor)
{
    public string BaseUrl
    {
        get
        {
            var request = httpContextAccessor.HttpContext?.Request;
            return PublicUrl.ResolveBase(options.Value.PublicBaseUrl, request?.Scheme, request?.Host.Value);
        }
    }

    /// <summary>True when the address comes from configuration rather than from the request.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.PublicBaseUrl);

    public string Absolute(string path) => PublicUrl.Combine(BaseUrl, path);
}

public static class PublicUrl
{
    /// <summary>The configured address when it is a usable http(s) URL, otherwise the request's own
    /// scheme and host. Throws when neither is available (a background task with nothing configured).</summary>
    public static string ResolveBase(string? configured, string? requestScheme, string? requestHost)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var trimmed = configured.Trim().TrimEnd('/');
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            {
                return trimmed;
            }

            throw new InvalidOperationException(
                $"App:PublicBaseUrl is '{configured}', which is not an http(s) address.");
        }

        if (!string.IsNullOrWhiteSpace(requestScheme) && !string.IsNullOrWhiteSpace(requestHost))
        {
            return $"{requestScheme}://{requestHost}";
        }

        throw new InvalidOperationException(
            "The app's public address is unknown: set App:PublicBaseUrl.");
    }

    public static string Combine(string baseUrl, string path) =>
        baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
}
