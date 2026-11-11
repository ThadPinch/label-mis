using System.Security.Cryptography;
using System.Text;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.QuickBooks;
using LabelsMis.Domain.Security;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.QuickBooks;

/// <summary>Hands out a live access token for the connected company and is the only place a token
/// is refreshed. Intuit can invalidate a refresh token that is used twice at once, so refreshes go
/// one at a time: a gate inside this process, and a row lock for the minutes during a deploy when
/// two copies of the app run side by side.</summary>
public class QuickBooksTokenService(
    IServiceScopeFactory scopeFactory,
    ISecretProtector protector,
    QuickBooksSyncLogger log,
    TimeProvider time)
{
    public const string NotConnectedMessage = "QuickBooks is not connected. Connect it under Setup, QuickBooks.";
    public const string NeedsReconnectMessage =
        "QuickBooks stopped accepting the saved sign-in. Connect again under Setup, QuickBooks.";

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>A token that is good right now, refreshing first when the saved one has run out.
    /// Throws <see cref="QuickBooksNotConnectedException"/> when no company is connected or a
    /// reconnect is needed.</summary>
    public async Task<QuickBooksCompanyAccess> GetAccessAsync(
        Guid actorId, bool automatic = false, CancellationToken cancellationToken = default)
    {
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LabelsMisDbContext>();
            var saved = await db.QuickBooksConnections.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            RequireUsable(saved, allowNeedsReconnect: false);
            if (saved!.AccessTokenUsable(time.GetUtcNow().UtcDateTime))
            {
                return Access(saved, protector.Unprotect(saved.AccessTokenProtected!));
            }
        }

        return await RefreshLockedAsync(force: false, refusedAccessToken: null, actorId, automatic, cancellationToken);
    }

    /// <summary>Refreshes now, whatever the saved token's age: the daily keep-alive, the Test
    /// button, and the retry after QuickBooks answers 401. Pass the token QuickBooks just refused
    /// as <paramref name="refusedAccessToken"/>; if someone else has refreshed since, theirs is
    /// returned and Intuit is not called again. Unlike <see cref="GetAccessAsync"/> this also tries
    /// when a reconnect is flagged, so Test can bring a connection back after an Intuit outage.</summary>
    public Task<QuickBooksCompanyAccess> RefreshAsync(
        Guid actorId, bool automatic = false, string? refusedAccessToken = null,
        CancellationToken cancellationToken = default) =>
        RefreshLockedAsync(force: true, refusedAccessToken, actorId, automatic, cancellationToken);

    /// <summary>Runs <paramref name="action"/> while no refresh is in flight, so a connect or a
    /// disconnect can't interleave with one. Not re-entrant: don't ask for a token inside it.</summary>
    public async Task<T> WithoutRefreshAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await action();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<QuickBooksCompanyAccess> RefreshLockedAsync(
        bool force, string? refusedAccessToken, Guid actorId, bool automatic, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LabelsMisDbContext>();
            var auth = scope.ServiceProvider.GetRequiredService<IQuickBooksAuthClient>();

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var connection = (await db.QuickBooksConnections
                    .FromSqlRaw("""SELECT * FROM "QuickBooksConnection" FOR UPDATE""")
                    .ToListAsync(cancellationToken))
                .SingleOrDefault();
            RequireUsable(connection, allowNeedsReconnect: force);

            // Whoever held the lock before us may already have done the work.
            if (connection!.AccessTokenUsable(time.GetUtcNow().UtcDateTime))
            {
                var current = protector.Unprotect(connection.AccessTokenProtected!);
                if (!force || (refusedAccessToken is not null && !SameToken(current, refusedAccessToken)))
                {
                    return Access(connection, current);
                }
            }

            var keys = new QuickBooksAppKeys(connection.ClientId, protector.Unprotect(connection.ClientSecretProtected));
            var refreshToken = protector.Unprotect(connection.RefreshTokenProtected!);

            QuickBooksTokenResponse tokens;
            try
            {
                tokens = await auth.RefreshAsync(keys, refreshToken, cancellationToken);
            }
            catch (QuickBooksAuthException ex)
            {
                var failedAt = time.GetUtcNow().UtcDateTime;
                var ended = false;
                if (ex.IsInvalidGrant)
                {
                    ended = connection.RecordRefreshRefusal(ex.Message, failedAt);
                }
                else
                {
                    connection.RecordError(ex.Message, failedAt);
                }

                await db.SaveChangesAsync(CancellationToken.None);
                await transaction.CommitAsync(CancellationToken.None);
                await log.WriteAsync(QuickBooksLogEntry.Failed(QuickBooksOperations.TokenRefresh, actorId, ex, automatic));
                if (ended)
                {
                    throw new QuickBooksNotConnectedException(NeedsReconnectMessage);
                }

                throw;
            }

            // Intuit has now retired the old refresh token, so the new pair is saved whatever
            // happens to the request that asked for it: no cancellation from here to the commit.
            var now = time.GetUtcNow().UtcDateTime;
            connection.StoreRefreshedTokens(
                protector.Protect(tokens.AccessToken),
                now.AddSeconds(tokens.ExpiresInSeconds),
                protector.Protect(tokens.RefreshToken),
                now.AddSeconds(tokens.RefreshTokenExpiresInSeconds),
                tokens.RefreshTokenHardExpiresInSeconds is { } hard ? now.AddSeconds(hard) : null,
                now);
            await db.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);

            await log.WriteAsync(new QuickBooksLogEntry(
                QuickBooksOperations.TokenRefresh, true, actorId, automatic, HttpStatus: 200, IntuitTid: tokens.IntuitTid));
            return Access(connection, tokens.AccessToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void RequireUsable(QuickBooksConnection? connection, bool allowNeedsReconnect)
    {
        if (connection is null || !connection.IsLinked)
        {
            throw new QuickBooksNotConnectedException(NotConnectedMessage);
        }

        if (connection.NeedsReconnect && !allowNeedsReconnect)
        {
            throw new QuickBooksNotConnectedException(NeedsReconnectMessage);
        }
    }

    private QuickBooksCompanyAccess Access(QuickBooksConnection connection, string accessToken) =>
        new(connection.Environment, protector.Unprotect(connection.RealmIdProtected!), accessToken);

    private static bool SameToken(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
