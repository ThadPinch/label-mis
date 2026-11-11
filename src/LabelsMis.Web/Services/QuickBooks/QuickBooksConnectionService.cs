using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
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
    DateTime? LastErrorAt)
{
    /// <summary>False until the deployment has a Secrets:Key; nothing can be saved or connected
    /// without one.</summary>
    public bool CanStoreSecrets => KeyProblem is null;

    public bool HasCredentials => State != QuickBooksConnectionState.NoCredentials;

    public static QuickBooksConnectionView Empty(string? keyProblem) => new(
        QuickBooksConnectionState.NoCredentials, keyProblem, QuickBooksEnvironment.Sandbox,
        string.Empty, string.Empty, null, null, null, null, null, null, null, null);
}

/// <summary>Owns the single <see cref="QuickBooksConnection"/> row: the Intuit app keys and, from
/// Q4 on, the OAuth link to a company.</summary>
public class QuickBooksConnectionService(
    LabelsMisDbContext db,
    ICurrentUserService currentUser,
    ISecretProtector protector,
    TimeProvider time)
{
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
            connection.LastErrorAt);
    }

    /// <summary>Saves the Intuit app keys; a blank secret keeps the one on file. Returns true when
    /// the company link was dropped because the app or environment changed.</summary>
    public async Task<bool> SaveCredentialsAsync(
        QuickBooksEnvironment environment,
        string clientId,
        string? clientSecret,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new InvalidOperationException("User is not authenticated.");
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

    /// <summary>The last four characters, or nothing for a secret too short to show part of.</summary>
    public static string Tail(string secret) => secret.Length >= 12 ? secret[^4..] : string.Empty;
}
