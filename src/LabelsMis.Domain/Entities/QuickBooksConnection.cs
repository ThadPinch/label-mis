using LabelsMis.Domain.Common;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Entities;

/// <summary>The link between LabelShop and one QuickBooks Online company (single row): the Intuit
/// app's keys, which company was authorised, and the OAuth tokens. Every secret on this row is
/// stored already encrypted; the entity never sees a plaintext token.</summary>
public class QuickBooksConnection : EntityBase
{
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-0000000000b2");

    /// <summary>Intuit changes the refresh token's value about every 24 hours and expires one that
    /// goes unused for 100 days, so the connection is refreshed at least this often.</summary>
    public static readonly TimeSpan KeepAliveInterval = TimeSpan.FromHours(24);

    /// <summary>An access token this close to expiry is treated as expired, so a request never
    /// leaves with a token that dies on the way.</summary>
    public static readonly TimeSpan AccessTokenSkew = TimeSpan.FromMinutes(2);

    /// <summary>How long QuickBooks may keep refusing the saved sign-in before LabelShop stops and
    /// asks for a reconnect. Intuit's token service also answers "invalid_grant" during some of its
    /// own outages, so a single refusal is not proof the connection is gone.</summary>
    public static readonly TimeSpan RefusalGrace = TimeSpan.FromMinutes(10);

    public const int MaxErrorLength = 1000;
    public const int MaxCompanyCheckLength = 4000;

    private QuickBooksConnection()
    {
    }

    public QuickBooksEnvironment Environment { get; private set; }
    public string ClientId { get; private set; } = string.Empty;
    public string ClientSecretProtected { get; private set; } = string.Empty;

    /// <summary>Last four characters of the client secret, kept in the clear so the settings page
    /// can show which secret is saved.</summary>
    public string ClientSecretTail { get; private set; } = string.Empty;

    /// <summary>Intuit's id for the connected company.</summary>
    public string? RealmId { get; private set; }
    public string? CompanyName { get; private set; }
    public string? AccessTokenProtected { get; private set; }
    public DateTime? AccessTokenExpiresAt { get; private set; }
    public string? RefreshTokenProtected { get; private set; }

    /// <summary>Rolls forward each time the token is refreshed (100 days of no use ends it).</summary>
    public DateTime? RefreshTokenExpiresAt { get; private set; }

    /// <summary>Intuit's hard limit (five years from authorisation); refreshing does not move it.</summary>
    public DateTime? RefreshTokenHardExpiresAt { get; private set; }

    public DateTime? ConnectedAt { get; private set; }
    public Guid? ConnectedById { get; private set; }
    public DateTime? LastRefreshedAt { get; private set; }
    public bool NeedsReconnect { get; private set; }
    public string? LastError { get; private set; }
    public DateTime? LastErrorAt { get; private set; }

    /// <summary>When QuickBooks first refused the refresh token now on file. Cleared by the next
    /// refresh that works.</summary>
    public DateTime? RefusedSince { get; private set; }

    /// <summary>The last company check, as <c>QuickBooksCompanyCheck.ToJson</c> wrote it: a handful
    /// of on/off settings and custom field names, no records.</summary>
    public string? CompanyCheckJson { get; private set; }
    public DateTime? CompanyCheckedAt { get; private set; }

    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecretProtected);

    /// <summary>A company has been authorised and its refresh token is on file.</summary>
    public bool IsLinked => RealmId is not null && RefreshTokenProtected is not null;

    public bool IsConnected => IsLinked && !NeedsReconnect;

    public QuickBooksConnectionState State =>
        !HasCredentials ? QuickBooksConnectionState.NoCredentials
        : NeedsReconnect ? QuickBooksConnectionState.NeedsReconnect
        : IsLinked ? QuickBooksConnectionState.Connected
        : QuickBooksConnectionState.NotConnected;

    /// <summary>The date by which someone has to press Connect again, whichever limit comes first.</summary>
    public DateTime? ReconnectBy => (RefreshTokenExpiresAt, RefreshTokenHardExpiresAt) switch
    {
        (null, var hard) => hard,
        (var rolling, null) => rolling,
        var (rolling, hard) => rolling < hard ? rolling : hard
    };

    public static QuickBooksConnection Create(Guid createdById, DateTime createdAt)
    {
        var connection = new QuickBooksConnection { Environment = QuickBooksEnvironment.Sandbox };
        connection.SetCreated(SingletonId, createdById, createdAt);
        return connection;
    }

    public bool AccessTokenUsable(DateTime now) =>
        IsConnected
        && AccessTokenProtected is not null
        && AccessTokenExpiresAt is { } expires
        && expires - AccessTokenSkew > now;

    /// <summary>Time for the background refresh: a day since the last one, or a refusal that has
    /// waited out <see cref="RefusalGrace"/> and is due its second try.</summary>
    public bool KeepAliveDue(DateTime now) =>
        IsConnected
        && (LastRefreshedAt is not { } last
            || now - last >= KeepAliveInterval
            || (RefusedSince is { } since && now - since >= RefusalGrace));

    /// <summary>Saves the Intuit app keys. A blank secret keeps the one on file. Returns true when
    /// the company link was dropped: tokens belong to the app (client ID) and environment that
    /// issued them, so changing either makes them useless.</summary>
    public bool SetCredentials(
        QuickBooksEnvironment environment,
        string clientId,
        string? clientSecretProtected,
        string? clientSecretTail,
        Guid modifiedById,
        DateTime modifiedAt)
    {
        clientId = (clientId ?? string.Empty).Trim();
        if (clientId.Length == 0)
        {
            throw new ArgumentException("Client ID is required.");
        }

        var keepSecret = string.IsNullOrWhiteSpace(clientSecretProtected);
        if (keepSecret && string.IsNullOrWhiteSpace(ClientSecretProtected))
        {
            throw new ArgumentException("Client secret is required.");
        }

        // A new secret for the same app keeps the link: Intuit accepts the rotated secret with the
        // tokens already issued.
        var appChanged = HasCredentials
            && (environment != Environment || !string.Equals(clientId, ClientId, StringComparison.Ordinal));
        var dropped = appChanged && RealmId is not null;
        if (dropped)
        {
            ClearLink();
        }

        Environment = environment;
        ClientId = clientId;
        if (!keepSecret)
        {
            ClientSecretProtected = clientSecretProtected!;
            ClientSecretTail = (clientSecretTail ?? string.Empty).Trim();
        }

        SetModified(modifiedById, modifiedAt);
        return dropped;
    }

    /// <summary>Records a completed authorisation: the company and its first set of tokens.</summary>
    public void Connect(
        string realmId,
        string accessTokenProtected,
        DateTime accessTokenExpiresAt,
        string refreshTokenProtected,
        DateTime refreshTokenExpiresAt,
        DateTime? refreshTokenHardExpiresAt,
        Guid connectedById,
        DateTime connectedAt)
    {
        if (!HasCredentials)
        {
            throw new InvalidOperationException("Save the Intuit client ID and secret before connecting.");
        }

        if (string.IsNullOrWhiteSpace(realmId))
        {
            throw new ArgumentException("QuickBooks did not say which company was connected.");
        }

        RequireToken(accessTokenProtected, "access");
        RequireToken(refreshTokenProtected, "refresh");

        var sameCompany = string.Equals(RealmId, realmId.Trim(), StringComparison.Ordinal);
        RealmId = realmId.Trim();
        if (!sameCompany)
        {
            CompanyName = null;
            CompanyCheckJson = null;
            CompanyCheckedAt = null;
        }

        AccessTokenProtected = accessTokenProtected;
        AccessTokenExpiresAt = accessTokenExpiresAt;
        RefreshTokenProtected = refreshTokenProtected;
        RefreshTokenExpiresAt = refreshTokenExpiresAt;
        RefreshTokenHardExpiresAt = refreshTokenHardExpiresAt;
        ConnectedAt = connectedAt;
        ConnectedById = connectedById;
        LastRefreshedAt = connectedAt;
        NeedsReconnect = false;
        RefusedSince = null;
        LastError = null;
        LastErrorAt = null;
        SetModified(connectedById, connectedAt);
    }

    /// <summary>Stores the tokens a refresh returned. Intuit may hand back a new refresh token; the
    /// newest one must be on file before the access token is used.</summary>
    public void StoreRefreshedTokens(
        string accessTokenProtected,
        DateTime accessTokenExpiresAt,
        string refreshTokenProtected,
        DateTime refreshTokenExpiresAt,
        DateTime? refreshTokenHardExpiresAt,
        DateTime refreshedAt)
    {
        if (!IsLinked)
        {
            throw new InvalidOperationException("QuickBooks is not connected.");
        }

        RequireToken(accessTokenProtected, "access");
        RequireToken(refreshTokenProtected, "refresh");

        AccessTokenProtected = accessTokenProtected;
        AccessTokenExpiresAt = accessTokenExpiresAt;
        RefreshTokenProtected = refreshTokenProtected;
        RefreshTokenExpiresAt = refreshTokenExpiresAt;
        RefreshTokenHardExpiresAt = refreshTokenHardExpiresAt ?? RefreshTokenHardExpiresAt;
        LastRefreshedAt = refreshedAt;
        NeedsReconnect = false;
        RefusedSince = null;
        LastError = null;
        LastErrorAt = null;
    }

    public void SetCompanyName(string? companyName)
    {
        CompanyName = string.IsNullOrWhiteSpace(companyName) ? null : companyName.Trim();
    }

    /// <summary>Keeps the result of a company check so the settings page can show it without
    /// asking QuickBooks on every visit.</summary>
    public void StoreCompanyCheck(string json, DateTime checkedAt)
    {
        if (!IsLinked)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxCompanyCheckLength)
        {
            throw new ArgumentException("The company check result is empty or too long to keep.");
        }

        CompanyCheckJson = json;
        CompanyCheckedAt = checkedAt;
    }

    /// <summary>QuickBooks refused the refresh token. The first refusal only starts the clock and
    /// the connection stays up; a refusal that comes <see cref="RefusalGrace"/> or more after the
    /// first ends it. Returns true when a reconnect is now needed.</summary>
    public bool RecordRefreshRefusal(string reason, DateTime at)
    {
        if (!IsLinked)
        {
            return false;
        }

        if (RefusedSince is { } since && at - since >= RefusalGrace)
        {
            MarkNeedsReconnect(reason, at);
            return true;
        }

        RefusedSince ??= at;
        RecordError(reason, at);
        return false;
    }

    /// <summary>Forgets the company and its tokens; the app keys stay.</summary>
    public void Disconnect(Guid modifiedById, DateTime modifiedAt)
    {
        ClearLink();
        SetModified(modifiedById, modifiedAt);
    }

    /// <summary>QuickBooks refused the refresh token (revoked, expired, or the company was
    /// disconnected on Intuit's side). The company stays on the row so the page can say what was
    /// connected; the dead access token is dropped so nothing tries to use it.</summary>
    public void MarkNeedsReconnect(string reason, DateTime at)
    {
        if (!IsLinked)
        {
            return;
        }

        NeedsReconnect = true;
        AccessTokenProtected = null;
        AccessTokenExpiresAt = null;
        RecordError(reason, at);
    }

    public void RecordError(string message, DateTime at)
    {
        message = (message ?? string.Empty).Trim();
        LastError = message.Length > MaxErrorLength ? message[..MaxErrorLength] : message;
        LastErrorAt = at;
    }

    private void ClearLink()
    {
        RealmId = null;
        CompanyName = null;
        AccessTokenProtected = null;
        AccessTokenExpiresAt = null;
        RefreshTokenProtected = null;
        RefreshTokenExpiresAt = null;
        RefreshTokenHardExpiresAt = null;
        ConnectedAt = null;
        ConnectedById = null;
        LastRefreshedAt = null;
        NeedsReconnect = false;
        RefusedSince = null;
        LastError = null;
        LastErrorAt = null;
        CompanyCheckJson = null;
        CompanyCheckedAt = null;
    }

    private static void RequireToken(string value, string kind)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"QuickBooks did not return a usable {kind} token.");
        }
    }
}
