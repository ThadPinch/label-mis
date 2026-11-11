using System.Text.Json;
using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.QuickBooks;
using LabelsMis.Domain.Security;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabelsMis.Web.Services.QuickBooks;

/// <summary>What Setup → QuickBooks shows about the connection. Carries no secret.</summary>
public sealed record QuickBooksConnectionView(
    QuickBooksConnectionState State,
    string? KeyProblem,
    QuickBooksEnvironment Environment,
    string ClientId,
    string ClientSecretTail,
    string? CompanyName,
    string? RealmId,
    DateTime? ConnectedAt,
    string? ConnectedBy,
    DateTime? ReconnectBy,
    DateTime? LastRefreshedAt,
    string? LastError,
    DateTime? LastErrorAt,
    DateTime? RefusedSince,
    QuickBooksCompanyCheck? CompanyCheck,
    DateTime? CompanyCheckedAt)
{
    /// <summary>False until the deployment has a Secrets:Key; nothing can be saved or connected
    /// without one.</summary>
    public bool CanStoreSecrets => KeyProblem is null;

    public bool HasCredentials => State != QuickBooksConnectionState.NoCredentials;

    /// <summary>A company has been authorised, whether or not its sign-in still works.</summary>
    public bool IsLinked => State is QuickBooksConnectionState.Connected or QuickBooksConnectionState.NeedsReconnect;

    public static QuickBooksConnectionView Empty(string? keyProblem) => new(
        QuickBooksConnectionState.NoCredentials, keyProblem, QuickBooksEnvironment.Sandbox,
        string.Empty, string.Empty, null, null, null, null, null, null, null, null, null, null, null);
}

/// <summary><see cref="Warning"/> is set when the company was connected but its name or settings
/// could not be read straight afterwards.</summary>
public sealed record QuickBooksConnectResult(string? CompanyName, string? Warning);

public sealed record QuickBooksTestResult(string? CompanyName, QuickBooksCompanyCheck Check, int DurationMs);

/// <summary><see cref="RevokeProblem"/> is set when the tokens were removed here but Intuit could
/// not be told to cancel them.</summary>
public sealed record QuickBooksDisconnectResult(bool WasConnected, string? CompanyName, string? RevokeProblem);

/// <summary>Owns the single <see cref="QuickBooksConnection"/> row: the Intuit app keys and the
/// OAuth link to a company (connect, test, disconnect). Tokens are refreshed elsewhere, by
/// <see cref="QuickBooksTokenService"/>.</summary>
public class QuickBooksConnectionService(
    LabelsMisDbContext db,
    ICurrentUserService currentUser,
    ISecretProtector protector,
    IQuickBooksAuthClient authClient,
    QuickBooksTokenService tokens,
    QuickBooksGateway gateway,
    QuickBooksSyncLogger log,
    TimeProvider time)
{
    /// <summary>Where Intuit sends the browser back to. Registered with Intuit character for
    /// character, so it never changes without the Intuit app changing too.</summary>
    public const string CallbackPath = "/quickbooks/callback";

    private const string KeysMissingMessage = "Save the Intuit client ID and secret before connecting.";

    public async Task<QuickBooksConnectionView> GetViewAsync(CancellationToken cancellationToken = default)
    {
        var keyProblem = protector.IsConfigured ? null : protector.ConfigurationProblem ?? "Secrets:Key is not set.";
        var connection = await db.QuickBooksConnections.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (connection is null)
        {
            return QuickBooksConnectionView.Empty(keyProblem);
        }

        string? connectedBy = null;
        if (connection.ConnectedById is { } userId)
        {
            connectedBy = await db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.Email ?? u.UserName)
                .SingleOrDefaultAsync(cancellationToken);
        }

        return new QuickBooksConnectionView(
            connection.State,
            keyProblem,
            connection.Environment,
            connection.ClientId,
            connection.ClientSecretTail,
            connection.CompanyName,
            connection.RealmId,
            connection.ConnectedAt,
            connectedBy,
            connection.ReconnectBy,
            connection.LastRefreshedAt,
            connection.LastError,
            connection.LastErrorAt,
            connection.RefusedSince,
            QuickBooksCompanyCheck.FromJson(connection.CompanyCheckJson),
            connection.CompanyCheckedAt);
    }

    /// <summary>True when a company is linked and QuickBooks has stopped accepting its sign-in:
    /// the one state the invoice pages warn about.</summary>
    public Task<bool> NeedsReconnectAsync(CancellationToken cancellationToken = default) =>
        db.QuickBooksConnections.AsNoTracking()
            .AnyAsync(c => c.NeedsReconnect && c.RealmId != null, cancellationToken);

    /// <summary>Saves the Intuit app keys; a blank secret keeps the one on file. Returns true when
    /// the company link was dropped because the app or environment changed.</summary>
    public async Task<bool> SaveCredentialsAsync(
        QuickBooksEnvironment environment,
        string clientId,
        string? clientSecret,
        CancellationToken cancellationToken = default)
    {
        var userId = RequireUser();
        var now = time.GetUtcNow().UtcDateTime;

        clientSecret = string.IsNullOrWhiteSpace(clientSecret) ? null : clientSecret.Trim();
        var secretProtected = clientSecret is null ? null : protector.Protect(clientSecret);
        var secretTail = clientSecret is null ? null : Tail(clientSecret);

        var connection = await db.QuickBooksConnections.SingleOrDefaultAsync(cancellationToken);
        if (connection is null)
        {
            connection = QuickBooksConnection.Create(userId, now);
            db.QuickBooksConnections.Add(connection);
        }

        var dropped = connection.SetCredentials(environment, clientId, secretProtected, secretTail, userId, now);
        await db.SaveChangesAsync(cancellationToken);
        return dropped;
    }

    /// <summary>The Intuit address to send the browser to. Throws
    /// <see cref="InvalidOperationException"/>, worded for the page, when connecting can't start.</summary>
    public async Task<string> BeginConnectAsync(string redirectUri, string state, CancellationToken cancellationToken = default)
    {
        if (!protector.IsConfigured)
        {
            throw new InvalidOperationException(protector.ConfigurationProblem ?? "Secrets:Key is not set.");
        }

        var connection = await db.QuickBooksConnections.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (connection is null || !connection.HasCredentials)
        {
            throw new InvalidOperationException(KeysMissingMessage);
        }

        return authClient.BuildAuthorizeUrl(connection.ClientId, redirectUri, state);
    }

    /// <summary>Finishes a connect: trades Intuit's one-time code for tokens, stores them encrypted,
    /// then reads the company's name and settings. <paramref name="redirectUri"/> must be the one
    /// the connect started with.</summary>
    public async Task<QuickBooksConnectResult> CompleteConnectAsync(
        string code, string realmId, string redirectUri, CancellationToken cancellationToken = default)
    {
        var userId = RequireUser();
        await tokens.WithoutRefreshAsync(
            async () =>
            {
                var connection = await db.QuickBooksConnections.SingleOrDefaultAsync(cancellationToken);
                if (connection is null || !connection.HasCredentials)
                {
                    throw new InvalidOperationException(KeysMissingMessage);
                }

                var keys = new QuickBooksAppKeys(connection.ClientId, protector.Unprotect(connection.ClientSecretProtected));
                QuickBooksTokenResponse issued;
                try
                {
                    issued = await authClient.ExchangeCodeAsync(keys, code, redirectUri, cancellationToken);
                }
                catch (QuickBooksAuthException ex)
                {
                    await log.WriteAsync(QuickBooksLogEntry.Failed(QuickBooksOperations.Connect, userId, ex));
                    throw;
                }

                // The code is spent and can't be traded again, so the tokens are saved whatever
                // happens to the request from here.
                var now = time.GetUtcNow().UtcDateTime;
                connection.Connect(
                    realmId,
                    protector.Protect(issued.AccessToken),
                    now.AddSeconds(issued.ExpiresInSeconds),
                    protector.Protect(issued.RefreshToken),
                    now.AddSeconds(issued.RefreshTokenExpiresInSeconds),
                    issued.RefreshTokenHardExpiresInSeconds is { } hard ? now.AddSeconds(hard) : null,
                    userId,
                    now);
                await db.SaveChangesAsync(CancellationToken.None);
                await log.WriteAsync(new QuickBooksLogEntry(
                    QuickBooksOperations.Connect, true, userId,
                    HttpStatus: 200, Message: $"Company {connection.RealmId}", IntuitTid: issued.IntuitTid));
                return true;
            },
            cancellationToken);

        try
        {
            var test = await TestAsync(cancellationToken);
            return new QuickBooksConnectResult(test.CompanyName, null);
        }
        catch (Exception ex) when (ex is QuickBooksApiException or QuickBooksAuthException or QuickBooksNotConnectedException)
        {
            return new QuickBooksConnectResult(null, ex.Message);
        }
    }

    /// <summary>Asks QuickBooks for the company's name and settings and keeps both. When a reconnect
    /// is flagged (or a refusal is pending) it first tries the saved sign-in once more, which is
    /// how a connection comes back after an Intuit outage without anyone pressing Connect.</summary>
    public async Task<QuickBooksTestResult> TestAsync(CancellationToken cancellationToken = default)
    {
        var userId = RequireUser();
        var saved = await db.QuickBooksConnections.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (saved is null || !saved.IsLinked)
        {
            throw new QuickBooksNotConnectedException(QuickBooksTokenService.NotConnectedMessage);
        }

        if (saved.NeedsReconnect || saved.RefusedSince is not null)
        {
            await tokens.RefreshAsync(userId, cancellationToken: cancellationToken);
        }

        var company = await gateway.ReadAsync(
            $"companyinfo/{saved.RealmId}", QuickBooksOperations.CompanyRead, cancellationToken: cancellationToken);
        var preferences = await gateway.ReadAsync(
            "preferences", QuickBooksOperations.PreferencesRead, cancellationToken: cancellationToken);

        string? name;
        QuickBooksCompanyCheck check;
        try
        {
            name = QuickBooksCompanyInfo.ReadCompanyName(company.Body);
            check = QuickBooksCompanyCheck.FromPreferences(preferences.Body);
        }
        catch (JsonException ex)
        {
            throw new QuickBooksApiException(
                QuickBooksErrorKind.Other, "QuickBooks sent an answer LabelShop could not read.",
                preferences.StatusCode, [], preferences.IntuitTid, ex);
        }

        // Loaded only now: the calls above may have refreshed the tokens on this row.
        var connection = await db.QuickBooksConnections.SingleAsync(cancellationToken);
        if (name is not null)
        {
            connection.SetCompanyName(name);
        }

        connection.StoreCompanyCheck(check.ToJson(), time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(cancellationToken);
        return new QuickBooksTestResult(name ?? connection.CompanyName, check, company.DurationMs + preferences.DurationMs);
    }

    /// <summary>Tells Intuit to cancel the tokens, then forgets the company. The app keys stay. If
    /// Intuit can't be told, the tokens are still removed here.</summary>
    public async Task<QuickBooksDisconnectResult> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var userId = RequireUser();
        return await tokens.WithoutRefreshAsync(
            async () =>
            {
                var connection = await db.QuickBooksConnections.SingleOrDefaultAsync(cancellationToken);
                if (connection is null || !connection.IsLinked)
                {
                    return new QuickBooksDisconnectResult(false, null, null);
                }

                var company = connection.CompanyName;
                string? problem = null;
                try
                {
                    var keys = new QuickBooksAppKeys(connection.ClientId, protector.Unprotect(connection.ClientSecretProtected));
                    await authClient.RevokeAsync(keys, protector.Unprotect(connection.RefreshTokenProtected!), cancellationToken);
                }
                catch (Exception ex) when (ex is QuickBooksAuthException or SecretProtectionException)
                {
                    problem = ex.Message;
                }

                connection.Disconnect(userId, time.GetUtcNow().UtcDateTime);
                await db.SaveChangesAsync(CancellationToken.None);
                await log.WriteAsync(new QuickBooksLogEntry(
                    QuickBooksOperations.Disconnect, problem is null, userId,
                    Message: problem is null ? null : $"Removed here; QuickBooks was not told: {problem}"));
                return new QuickBooksDisconnectResult(true, company, problem);
            },
            cancellationToken);
    }

    /// <summary>The last four characters, or nothing for a secret too short to show part of.</summary>
    public static string Tail(string secret) => secret.Length >= 12 ? secret[^4..] : string.Empty;

    private Guid RequireUser() =>
        currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
}
