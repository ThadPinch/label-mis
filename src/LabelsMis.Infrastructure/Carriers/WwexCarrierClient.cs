using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using LabelsMis.Domain.Carriers;
using LabelsMis.Domain.Carriers.Wwex;
using LabelsMis.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LabelsMis.Infrastructure.Carriers;

/// <summary>Worldwide Express's SpeedShip API over plain HttpClient: UPS small package rates,
/// labels, voids and tracking. Request bodies and answers are read in the Domain
/// (<see cref="WwexRequests"/>, <see cref="WwexAnswers"/>); this class signs in, sends, and turns
/// every way of failing into a <see cref="CarrierException"/>. It logs the flow, the status and
/// WWEX's correlation id, never a body (addresses) and never a token.</summary>
public class WwexCarrierClient(
    HttpClient httpClient,
    WwexTokenCache tokens,
    IOptions<WwexOptions> options,
    TimeProvider time,
    ILogger<WwexCarrierClient> logger) : ICarrierClient
{
    private const string CarrierName = "Worldwide Express";
    private readonly WwexOptions _options = options.Value;

    public async Task<CarrierSignIn> SignInAsync(CarrierCredentials credentials, CancellationToken cancellationToken = default)
    {
        // Always a fresh sign-in: this is the check that the keys on file still work.
        tokens.Forget(credentials);
        var token = await GetTokenAsync(credentials, cancellationToken);
        return new CarrierSignIn(token.Account, token.Plan, token.ExpiresAtUtc);
    }

    public async Task<CarrierRateQuote> GetRatesAsync(
        CarrierCredentials credentials, CarrierRateRequest request, CancellationToken cancellationToken = default)
    {
        var answer = await PostAsync(credentials, WwexRequests.ShopFlow, WwexRequests.Shop(request), cancellationToken);
        return WwexAnswers.ParseRates(answer, request.Boxes);
    }

    public async Task<CarrierBooking> BookAsync(
        CarrierCredentials credentials, CarrierBookRequest request, CancellationToken cancellationToken = default)
    {
        var answer = await PostAsync(credentials, WwexRequests.BookFlow, WwexRequests.Book(request), cancellationToken);
        return WwexAnswers.ParseBooking(answer);
    }

    public async Task<CarrierLabel> GetLabelAsync(
        CarrierCredentials credentials,
        string quoteId,
        string? trackingNumber,
        CarrierLabelFormat format,
        CancellationToken cancellationToken = default)
    {
        if (format == CarrierLabelFormat.PdfLetter)
        {
            var all = await PostAsync(credentials, WwexRequests.DocumentsFlow, WwexRequests.Documents(quoteId), cancellationToken);
            return WwexAnswers.ParseLabel(all, format);
        }

        if (string.IsNullOrWhiteSpace(trackingNumber))
        {
            throw new ArgumentException("A single label needs the box's tracking number.", nameof(trackingNumber));
        }

        var one = await PostAsync(
            credentials,
            WwexRequests.LabelFlow,
            WwexRequests.Label(quoteId, trackingNumber.Trim(), zpl: format == CarrierLabelFormat.Zpl),
            cancellationToken);
        return WwexAnswers.ParseLabel(one, format);
    }

    public async Task<CarrierVoidOutcome> VoidAsync(
        CarrierCredentials credentials, string quoteId, CancellationToken cancellationToken = default)
    {
        var answer = await PostAsync(credentials, WwexRequests.CancelFlow, WwexRequests.Cancel(quoteId), cancellationToken);
        return WwexAnswers.ParseVoid(answer);
    }

    public async Task<CarrierTracking> TrackAsync(
        CarrierCredentials credentials, string trackingNumber, CancellationToken cancellationToken = default)
    {
        var answer = await PostAsync(credentials, WwexRequests.TrackFlow, WwexRequests.Track(trackingNumber), cancellationToken);
        return WwexAnswers.ParseTracking(answer);
    }

    /// <summary>Sends one flow with a live token. A 401 means the gateway turned the token away
    /// before anything ran, so one fresh sign-in and one more try is safe even for a booking.</summary>
    private async Task<string> PostAsync(
        CarrierCredentials credentials, string flow, string body, CancellationToken cancellationToken)
    {
        var address = _options.ApiBase(credentials.Environment) + flow;
        var token = await GetTokenAsync(credentials, cancellationToken);
        var (status, answer) = await SendAsync(flow, () => JsonRequest(address, body, token.AccessToken), cancellationToken);
        if (status == 401)
        {
            tokens.Forget(credentials);
            token = await GetTokenAsync(credentials, cancellationToken);
            (status, answer) = await SendAsync(flow, () => JsonRequest(address, body, token.AccessToken), cancellationToken);
        }

        if (status is < 200 or >= 300)
        {
            throw WwexAnswers.Failure(status, answer);
        }

        return answer;
    }

    private async Task<WwexToken> GetTokenAsync(CarrierCredentials credentials, CancellationToken cancellationToken)
    {
        if (tokens.Get(credentials, Now()) is { } cached)
        {
            return cached;
        }

        await tokens.Gate.WaitAsync(cancellationToken);
        try
        {
            if (tokens.Get(credentials, Now()) is { } raced)
            {
                return raced;
            }

            var (status, answer) = await SendAsync(
                "sign-in",
                () => new HttpRequestMessage(HttpMethod.Post, _options.TokenUrl(credentials.Environment))
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["grant_type"] = "client_credentials",
                        ["client_id"] = credentials.ClientId,
                        ["client_secret"] = credentials.ClientSecret,
                        ["audience"] = _options.Audience(credentials.Environment)
                    })
                },
                cancellationToken);
            if (status is < 200 or >= 300)
            {
                throw WwexAnswers.Failure(status, answer);
            }

            var token = WwexAnswers.ParseToken(answer, Now());
            tokens.Store(credentials, token);
            return token;
        }
        finally
        {
            tokens.Gate.Release();
        }
    }

    private static HttpRequestMessage JsonRequest(string address, string body, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, address)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private async Task<(int Status, string Body)> SendAsync(
        string flow, Func<HttpRequestMessage> build, CancellationToken cancellationToken)
    {
        using var request = build();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var response = await httpClient.SendAsync(request, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            var status = (int)response.StatusCode;
            logger.LogInformation(
                "WWEX {Flow} answered {Status} in {Elapsed} ms",
                flow,
                status,
                (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return (status, body);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("WWEX {Flow} could not be reached: {Reason}", flow, ex.Message);
            throw new CarrierException(CarrierErrorKind.NoAnswer, $"{CarrierName} could not be reached.", inner: ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("WWEX {Flow} did not answer within {Seconds} s", flow, _options.TimeoutSeconds);
            throw new CarrierException(CarrierErrorKind.NoAnswer, $"{CarrierName} did not answer in time.", inner: ex);
        }
    }

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
