using System.Security.Cryptography;
using LabelsMis.Domain.Security;
using LabelsMis.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace LabelsMis.Infrastructure.Tests.Security;

public class AesGcmSecretProtectorTests
{
    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static AesGcmSecretProtector Create(string? key) =>
        new(Options.Create(new SecretOptions { Key = key }));

    [Fact]
    public void Protect_ThenUnprotect_ReturnsTheOriginal()
    {
        var protector = Create(NewKey());

        var stored = protector.Protect("refresh-token-åß∂ 123");

        protector.Unprotect(stored).Should().Be("refresh-token-åß∂ 123");
    }

    [Fact]
    public void Protect_DoesNotStoreThePlaintext()
    {
        var protector = Create(NewKey());

        var stored = protector.Protect("AB11-very-secret-value");

        stored.Should().StartWith("v1:").And.NotContain("very-secret-value");
    }

    [Fact]
    public void Protect_SameValueTwice_GivesDifferentCiphertext()
    {
        var protector = Create(NewKey());

        protector.Protect("same").Should().NotBe(protector.Protect("same"));
    }

    [Fact]
    public void Protect_EmptyString_RoundTrips()
    {
        var protector = Create(NewKey());

        protector.Unprotect(protector.Protect(string.Empty)).Should().BeEmpty();
    }

    [Fact]
    public void Unprotect_WithAnotherKey_Throws()
    {
        var stored = Create(NewKey()).Protect("secret");

        var act = () => Create(NewKey()).Unprotect(stored);

        act.Should().Throw<SecretProtectionException>().WithMessage("*current Secrets:Key*");
    }

    [Fact]
    public void Unprotect_AlteredValue_Throws()
    {
        var protector = Create(NewKey());
        var stored = protector.Protect("secret");
        var bytes = Convert.FromBase64String(stored[3..]);
        bytes[^1] ^= 0x01;

        var act = () => protector.Unprotect("v1:" + Convert.ToBase64String(bytes));

        act.Should().Throw<SecretProtectionException>();
    }

    [Theory]
    [InlineData("plain text")]
    [InlineData("v1:not base64 !!")]
    [InlineData("v1:AAAA")]
    public void Unprotect_ValueThisAppDidNotWrite_Throws(string stored)
    {
        var act = () => Create(NewKey()).Unprotect(stored);

        act.Should().Throw<SecretProtectionException>();
    }

    [Theory]
    [InlineData(null, "*not set*")]
    [InlineData("", "*not set*")]
    [InlineData("   ", "*not set*")]
    [InlineData("not base64 !!", "*not valid base64*")]
    [InlineData("c2hvcnQ=", "*must be 32 bytes*")]
    public void Constructor_WithoutAUsableKey_IsNotConfiguredAndSaysWhy(string? key, string problem)
    {
        var protector = Create(key);

        protector.IsConfigured.Should().BeFalse();
        protector.ConfigurationProblem.Should().Match(problem);
    }

    [Fact]
    public void Protect_WhenNotConfigured_Throws()
    {
        var act = () => Create(null).Protect("secret");

        act.Should().Throw<SecretProtectionException>().WithMessage("*Secrets:Key is not set*");
    }

    [Fact]
    public void Constructor_WithA32ByteKey_IsConfigured()
    {
        var protector = Create(NewKey());

        protector.IsConfigured.Should().BeTrue();
        protector.ConfigurationProblem.Should().BeNull();
    }
}
