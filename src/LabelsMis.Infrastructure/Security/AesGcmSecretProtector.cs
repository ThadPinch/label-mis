using System.Security.Cryptography;
using System.Text;
using LabelsMis.Domain.Security;
using Microsoft.Extensions.Options;

namespace LabelsMis.Infrastructure.Security;

public class SecretOptions
{
    public const string SectionName = "Secrets";

    /// <summary>32 random bytes, base64. Set it as an encrypted environment variable
    /// (<c>Secrets__Key</c>); it must never live in the database or the repository.</summary>
    public string? Key { get; set; }
}

/// <summary>AES-256-GCM. A stored value is <c>v1:</c> + base64(nonce | tag | ciphertext); the
/// version prefix leaves room to rotate the scheme later.</summary>
public class AesGcmSecretProtector : ISecretProtector
{
    private const string Prefix = "v1:";
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[]? _key;

    public AesGcmSecretProtector(IOptions<SecretOptions> options)
    {
        var configured = options.Value.Key;
        if (string.IsNullOrWhiteSpace(configured))
        {
            ConfigurationProblem = "Secrets:Key is not set.";
            return;
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(configured.Trim());
        }
        catch (FormatException)
        {
            ConfigurationProblem = "Secrets:Key is not valid base64.";
            return;
        }

        if (key.Length != KeySize)
        {
            ConfigurationProblem = $"Secrets:Key must be {KeySize} bytes; it is {key.Length}.";
            return;
        }

        _key = key;
    }

    public bool IsConfigured => _key is not null;

    public string? ConfigurationProblem { get; }

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var key = RequireKey();

        var plain = Encoding.UTF8.GetBytes(plaintext);
        var output = new byte[NonceSize + TagSize + plain.Length];
        var nonce = output.AsSpan(0, NonceSize);
        var tag = output.AsSpan(NonceSize, TagSize);
        var cipher = output.AsSpan(NonceSize + TagSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);
        return Prefix + Convert.ToBase64String(output);
    }

    public string Unprotect(string protectedValue)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);
        var key = RequireKey();

        if (!protectedValue.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new SecretProtectionException("The stored secret is not in a format this app wrote.");
        }

        byte[] input;
        try
        {
            input = Convert.FromBase64String(protectedValue[Prefix.Length..]);
        }
        catch (FormatException ex)
        {
            throw new SecretProtectionException("The stored secret is damaged.", ex);
        }

        if (input.Length < NonceSize + TagSize)
        {
            throw new SecretProtectionException("The stored secret is damaged.");
        }

        var plain = new byte[input.Length - NonceSize - TagSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(
                input.AsSpan(0, NonceSize),
                input.AsSpan(NonceSize + TagSize),
                input.AsSpan(NonceSize, TagSize),
                plain);
        }
        catch (CryptographicException ex)
        {
            throw new SecretProtectionException(
                "The stored secret could not be read with the current Secrets:Key.", ex);
        }

        return Encoding.UTF8.GetString(plain);
    }

    private byte[] RequireKey() =>
        _key ?? throw new SecretProtectionException(ConfigurationProblem ?? "Secrets:Key is not set.");
}
