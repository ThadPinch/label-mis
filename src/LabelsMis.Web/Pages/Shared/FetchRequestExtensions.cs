namespace LabelsMis.Web.Pages.Shared;

/// <summary>
/// Tells an in-page <c>fetch()</c> post apart from an ordinary form post. job-action-modal.js sends
/// <c>X-Requested-With: fetch</c> on every request it makes, so a handler can hand back a refreshed
/// partial for the popup instead of the redirect that a full-page post needs.
/// </summary>
public static class FetchRequestExtensions
{
    public const string HeaderValue = "fetch";

    public static bool IsFetch(this HttpRequest request) =>
        string.Equals(request.Headers.XRequestedWith.ToString(), HeaderValue, StringComparison.OrdinalIgnoreCase);
}
