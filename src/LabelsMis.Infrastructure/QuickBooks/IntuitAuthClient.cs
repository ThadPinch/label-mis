using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LabelsMis.Domain.QuickBooks;
using Microsoft.Extensions.Options;

namespace LabelsMis.Infrastructure.QuickBooks;

/// <summary>Intuit's OAuth 2.0 endpoints over plain HttpClient. Nothing here is logged: every
/// request and response carries a secret.</summary>
public class IntuitAuthClient(HttpClient httpClient, IOptions<QuickBooksOptions> options) : IQuickBooksAuthClient
{
    private readonly QuickBooksOptions _options = options.Value;

    public string BuildAuthorizeUrl(string clientId, string redirectUri, string state) =>
        _options.AuthorizeUrl
        + "?client_id=" + Uri.EscapeDataString(clientId)
        + "&response_type=code"
        + "&scope=" + Uri.EscapeDataString(_options.Scope)
        + "&redirect_uri=" + Uri.EscapeDataString(redirectUri)
        + "&state=" + Uri.EscapeDataString(state);

    public Task<QuickBooksTokenResponse> ExchangeCodeAsync(
        QuickBooksAppKeys keys, string code, string redirectUri, CancellationToken cancellationToken = default) =>
        PostTokenAsync(
            keys,
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri
            },
            cancellationToken);

    public Task<QuickBooksTokenResponse> RefreshAsync(
        QuickBooksAppKeys keys, string refreshToken, CancellationToken cancellationToken = default) =>
        PostTokenAsync(
            keys,
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            },
            cancellationToken);

    public async Task RevokeAsync(QuickBooksAppKeys keys, string token, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.RevokeUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { token }), Encoding.UTF8, "application/json")
        };
        var (status, body, tid) = await SendAsync(request, keys, cancellationToken);
        if (status is < 200 or >= 300)
        {
            throw QuickBooksAuthException.FromResponse(status, body, tid);
        }
    }

    private async Task<QuickBooksTokenResponse> PostTokenAsync(
        QuickBooksAppKeys keys, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenUrl)
        {
            Content = new FormUrlEncodedContent(form)
        };

        // Asks Intuit to include the refresh token's five-year hard limit in the answer.
        request.Headers.TryAddWithoutValidation("x-include-refresh-token-hard-expires-in", "true");

        var (status, body, tid) = await SendAsync(request, keys, cancellationToken);
        if (status is < 200 or >= 300)
        {
            throw QuickBooksAuthException.FromResponse(status, body, tid);
        }

        return QuickBooksTokenResponse.Parse(body, tid);
    }

    private async Task<(int Status, string Body, string? IntuitTid)> SendAsync(
        HttpRequestMessage request, QuickBooksAppKeys keys, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{keys.ClientId}:{keys.ClientSecret}")));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));
        try
        {
            using var response = await httpClient.SendAsync(request, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return ((int)response.StatusCode, body, QuickBooksApiClient.IntuitTid(response));
        }
        catch (HttpRequestException ex)
        {
            throw new QuickBooksAuthException("QuickBooks sign-in could not be reached.", null, null, null, ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new QuickBooksAuthException("QuickBooks sign-in did not answer in time.", null, null, null, ex);
        }
    }
}
