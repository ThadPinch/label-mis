using LabelsMis.Domain.QuickBooks;

namespace LabelsMis.Web.Services.QuickBooks;

/// <summary>Who a QuickBooks call is made for, and which LabelShop record it is about, so the
/// activity log can show an invoice's or a customer's own history. <see cref="Automatic"/> marks
/// background work.</summary>
public sealed record QuickBooksCaller(Guid ActorId, bool Automatic = false, Guid? InvoiceId = null, Guid? CustomerId = null);

/// <summary>The one door every QuickBooks call goes through. It adds what a bare API call lacks:
/// a live token, one retry when QuickBooks refuses the token early, and a row in the activity log
/// for every attempt, worked or not.</summary>
public class QuickBooksGateway(
    QuickBooksTokenService tokens,
    IQuickBooksApiClient api,
    QuickBooksSyncLogger log,
    ICurrentUserService currentUser)
{
    /// <summary>The signed-in user as the caller, for a call about one invoice or one customer.</summary>
    public QuickBooksCaller CallerFor(Guid? invoiceId = null, Guid? customerId = null) =>
        new(currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated."), false, invoiceId, customerId);

    public Task<QuickBooksApiResponse> ReadAsync(
        string resource,
        string operation,
        QuickBooksCaller? caller = null,
        IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(operation, caller, access => api.ReadAsync(access, resource, parameters, cancellationToken), cancellationToken);

    public Task<QuickBooksApiResponse> QueryAsync(
        string query,
        string operation,
        QuickBooksCaller? caller = null,
        IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(operation, caller, access => api.QueryAsync(access, query, parameters, cancellationToken), cancellationToken);

    public Task<QuickBooksApiResponse> WriteAsync(
        string resource,
        string? jsonBody,
        string operation,
        string? requestId = null,
        IReadOnlyDictionary<string, string>? parameters = null,
        QuickBooksCaller? caller = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            operation,
            caller,
            access => api.WriteAsync(access, resource, jsonBody, requestId, parameters, cancellationToken),
            cancellationToken);

    private async Task<QuickBooksApiResponse> SendAsync(
        string operation,
        QuickBooksCaller? caller,
        Func<QuickBooksCompanyAccess, Task<QuickBooksApiResponse>> call,
        CancellationToken cancellationToken)
    {
        var who = caller ?? new QuickBooksCaller(
            currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated."));
        try
        {
            var access = await tokens.GetAccessAsync(who.ActorId, who.Automatic, cancellationToken);
            QuickBooksApiResponse response;
            try
            {
                response = await call(access);
            }
            catch (QuickBooksApiException ex) when (ex.Kind == QuickBooksErrorKind.Unauthorized)
            {
                // The token was refused before its hour was up (revoked, or replaced by another
                // copy of the app). One refresh, one more try; a second 401 is the answer.
                access = await tokens.RefreshAsync(who.ActorId, who.Automatic, access.AccessToken, cancellationToken);
                response = await call(access);
            }

            await log.WriteAsync(QuickBooksLogEntry.Ok(
                operation, who.ActorId, who.Automatic, response, invoiceId: who.InvoiceId, customerId: who.CustomerId));
            return response;
        }
        catch (Exception ex) when (ex is QuickBooksApiException or QuickBooksAuthException or QuickBooksNotConnectedException)
        {
            await log.WriteAsync(QuickBooksLogEntry.Failed(
                operation, who.ActorId, ex, who.Automatic, who.InvoiceId, who.CustomerId));
            throw;
        }
    }
}
