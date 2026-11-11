namespace LabelsMis.Domain.Security;

/// <summary>Encrypts secrets the app must be able to read back (OAuth tokens, client secrets)
/// before they are stored. The key lives outside the database.</summary>
public interface ISecretProtector
{
    /// <summary>False when no usable key is configured; <see cref="Protect"/> and
    /// <see cref="Unprotect"/> then throw <see cref="SecretProtectionException"/>.</summary>
    bool IsConfigured { get; }

    /// <summary>Why <see cref="IsConfigured"/> is false, in words a person can act on.</summary>
    string? ConfigurationProblem { get; }

    string Protect(string plaintext);

    /// <summary>Throws <see cref="SecretProtectionException"/> when the value was not produced
    /// with the current key or has been altered.</summary>
    string Unprotect(string protectedValue);
}

public class SecretProtectionException(string message, Exception? inner = null) : Exception(message, inner);
