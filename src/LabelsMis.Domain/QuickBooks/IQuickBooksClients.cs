using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.QuickBooks;

/// <summary>The Intuit app's keys, in the clear, for the length of one call.</summary>
public sealed record QuickBooksAppKeys(string ClientId, string ClientSecret);

/// <summary>What a call to a company needs: which world, which company, and a live access token.</summary>
public sealed record QuickBooksCompanyAccess(QuickBooksEnvironment Environment, string RealmId, string AccessToken);

/// <summary>A successful answer from the Accounting API. <see cref="IntuitTid"/> is the trace id
/// Intuit support asks for.</summary>
public sealed record QuickBooksApiResponse(int StatusCode, string Body, string? IntuitTid, int DurationMs);

/// <summary>Intuit's OAuth 2.0 endpoints. Failures throw <see cref="QuickBooksAuthException"/>.</summary>
public interface IQuickBooksAuthClient
{
    /// <summary>The address to send the browser to so a QuickBooks admin can approve the app.</summary>
    string BuildAuthorizeUrl(string clientId, string redirectUri, string state);

    Task<QuickBooksTokenResponse> ExchangeCodeAsync(
        QuickBooksAppKeys keys, string code, string redirectUri, CancellationToken cancellationToken = default);

    Task<QuickBooksTokenResponse> RefreshAsync(
        QuickBooksAppKeys keys, string refreshToken, CancellationToken cancellationToken = default);

    Task RevokeAsync(QuickBooksAppKeys keys, string token, CancellationToken cancellationToken = default);
}

/// <summary>The QuickBooks Online Accounting API for one company. Anything but a 2xx throws
/// <see cref="QuickBooksApiException"/>.</summary>
public interface IQuickBooksApiClient
{
    /// <summary>GET a resource, e.g. <c>preferences</c> or <c>companyinfo/123</c>. Metered by Intuit.</summary>
    Task<QuickBooksApiResponse> ReadAsync(
        QuickBooksCompanyAccess access, string resource, CancellationToken cancellationToken = default);

    /// <summary>Run a query, e.g. <c>select Id from Invoice where DocNumber = 'INV-1'</c>. Metered.</summary>
    Task<QuickBooksApiResponse> QueryAsync(
        QuickBooksCompanyAccess access, string query, CancellationToken cancellationToken = default);

    /// <summary>POST a create, update, void or send. <paramref name="requestId"/> makes a repeat of
    /// the same call return the first answer instead of acting twice.</summary>
    Task<QuickBooksApiResponse> WriteAsync(
        QuickBooksCompanyAccess access,
        string resource,
        string? jsonBody,
        string? requestId = null,
        IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default);
}
