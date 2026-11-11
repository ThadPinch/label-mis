using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.QuickBooks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LabelsMis.Infrastructure.QuickBooks;

/// <summary>The QuickBooks Online Accounting API over plain HttpClient. Logs the resource, status,
/// duration and Intuit's trace id; never a token, a query or a body (Intuit's security rules
/// forbid QuickBooks data in logs).</summary>
public class QuickBooksApiClient(
    HttpClient httpClient,
    IOptions<QuickBooksOptions> options,
    ILogger<QuickBooksApiClient> logger) : IQuickBooksApiClient
{
    private readonly QuickBooksOptions _options = options.Value;

    public Task<QuickBooksApiResponse> ReadAsync(
        QuickBooksCompanyAccess access, string resource, CancellationToken cancellationToken = default) =>
        SendWithReadRetriesAsync(
            access, () => new HttpRequestMessage(HttpMethod.Get, Url(access, resource, null)), resource, cancellationToken);

    public Task<QuickBooksApiResponse> QueryAsync(
        QuickBooksCompanyAccess access, string query, CancellationToken cancellationToken = default) =>
        SendWithReadRetriesAsync(
            access,
            () => new HttpRequestMessage(
                HttpMethod.Get, Url(access, "query", new Dictionary<string, string> { ["query"] = query })),
            "query",
            cancellationToken);

    public Task<QuickBooksApiResponse> WriteAsync(
        QuickBooksCompanyAccess access,
        string resource,
        string? jsonBody,
        string? requestId = null,
        IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var all = new Dictionary<string, string>(parameters ?? new Dictionary<string, string>());
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            all["requestid"] = requestId;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, Url(access, resource, all))
        {
            // The send and void calls take no body; Intuit asks for octet-stream on those.
            Content = jsonBody is null
                ? new ByteArrayContent([]) { Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") } }
                : new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };
        return SendAsync(access, request, resource, cancellationToken);
    }

    /// <summary>Intuit's trace id for a response, whichever spelling of the header it used.</summary>
    public static string? IntuitTid(HttpResponseMessage response) =>
        response.Headers.TryGetValues("intuit_tid", out var values) ? values.FirstOrDefault() : null;

    private async Task<QuickBooksApiResponse> SendWithReadRetriesAsync(
        QuickBooksCompanyAccess access,
        Func<HttpRequestMessage> build,
        string resource,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await SendAsync(access, build(), resource, cancellationToken);
            }
            catch (QuickBooksApiException ex) when (ex.IsTransient && attempt < _options.ReadRetryDelaysMs.Length)
            {
                await Task.Delay(Math.Max(0, _options.ReadRetryDelaysMs[attempt]), cancellationToken);
            }
        }
    }

    private async Task<QuickBooksApiResponse> SendAsync(
        QuickBooksCompanyAccess access, HttpRequestMessage request, string resource, CancellationToken cancellationToken)
    {
        using (request)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access.AccessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var response = await httpClient.SendAsync(request, timeout.Token);
                var body = await response.Content.ReadAsStringAsync(timeout.Token);
                var status = (int)response.StatusCode;
                var tid = IntuitTid(response);
                var ms = (int)stopwatch.ElapsedMilliseconds;
                logger.LogInformation(
                    "QuickBooks {Method} {Resource} -> {Status} in {Ms} ms (intuit_tid {Tid})",
                    request.Method.Method, ResourceName(resource), status, ms, tid);

                if (status is < 200 or >= 300)
                {
                    throw QuickBooksApiException.FromResponse(status, body, tid);
                }

                return new QuickBooksApiResponse(status, body, tid, ms);
            }
            catch (HttpRequestException ex)
            {
                throw QuickBooksApiException.Network("QuickBooks could not be reached.", ex);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw QuickBooksApiException.Network("QuickBooks did not answer in time.", ex);
            }
        }
    }

    private string Url(QuickBooksCompanyAccess access, string resource, IReadOnlyDictionary<string, string>? parameters)
    {
        var apiBase = access.Environment == QuickBooksEnvironment.Production
            ? _options.ProductionApiBase
            : _options.SandboxApiBase;
        var url = new StringBuilder(apiBase.TrimEnd('/'))
            .Append("/v3/company/").Append(Uri.EscapeDataString(access.RealmId))
            .Append('/').Append(resource.TrimStart('/'))
            .Append("?minorversion=").Append(_options.MinorVersion);
        foreach (var (key, value) in parameters ?? new Dictionary<string, string>())
        {
            url.Append('&').Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
        }

        return url.ToString();
    }

    /// <summary>The entity part of a resource (<c>invoice/145/send</c> → <c>invoice</c>), so ids do
    /// not reach the log.</summary>
    private static string ResourceName(string resource)
    {
        var trimmed = resource.TrimStart('/');
        var slash = trimmed.IndexOf('/');
        return slash < 0 ? trimmed : trimmed[..slash];
    }
}
