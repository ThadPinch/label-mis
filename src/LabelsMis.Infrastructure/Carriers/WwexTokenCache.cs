using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using LabelsMis.Domain.Carriers;
using LabelsMis.Domain.Carriers.Wwex;

namespace LabelsMis.Infrastructure.Carriers;

/// <summary>Keeps the sign-in token between calls. A WWEX token is good for a day and a new one is
/// had by asking, so it lives in memory only: nothing about it is stored, and a restart just signs
/// in again. Keyed by environment, client ID and a hash of the secret, so new keys never ride on an
/// old token.</summary>
public class WwexTokenCache
{
    /// <summary>A token this close to its end is not handed out: a call must not leave with a
    /// token that dies on the way.</summary>
    public static readonly TimeSpan Skew = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, WwexToken> _tokens = new();

    /// <summary>One sign-in at a time, so a burst of calls with no token asks for one, not ten.</summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);

    public WwexToken? Get(CarrierCredentials credentials, DateTime nowUtc) =>
        _tokens.TryGetValue(Key(credentials), out var token) && token.ExpiresAtUtc - Skew > nowUtc ? token : null;

    public void Store(CarrierCredentials credentials, WwexToken token) => _tokens[Key(credentials)] = token;

    public void Forget(CarrierCredentials credentials) => _tokens.TryRemove(Key(credentials), out _);

    private static string Key(CarrierCredentials credentials) =>
        $"{credentials.Environment}|{credentials.ClientId}|"
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credentials.ClientSecret)));
}
