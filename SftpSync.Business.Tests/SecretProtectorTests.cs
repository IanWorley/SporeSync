using System.Security.Cryptography;
using SftpSync.Business.Service;

namespace SftpSync.Business.Tests;

public sealed class SecretProtectorTests
{
    [Fact]
    public void ProtectAndUnprotect_RoundTripSecret_WithInitializedKeyProvider()
    {
        var protector = CreateProtector();

        var protectedValue = protector.Protect("secret-password");
        var unprotectedValue = protector.Unprotect(protectedValue);

        Assert.Equal("secret-password", unprotectedValue);
        Assert.NotEqual("secret-password", protectedValue);
        Assert.StartsWith("v1:", protectedValue);
    }

    [Fact]
    public void Protect_Throws_WhenKeyProviderIsNotInitialized()
    {
        var protector = new SecretProtector(new EncryptionKeyProvider());

        var exception = Assert.Throws<InvalidOperationException>(() => protector.Protect("secret"));

        Assert.Equal("Encryption key has not been initialized.", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-protected")]
    [InlineData("v2:a:b:c")]
    [InlineData("v1:a:b")]
    public void Unprotect_Throws_WhenProtectedValueHasUnsupportedFormat(string protectedValue)
    {
        var protector = CreateProtector();

        var exception = Assert.Throws<InvalidOperationException>(() => protector.Unprotect(protectedValue));

        Assert.Equal("Secret value is not in a supported protected format.", exception.Message);
    }

    private static SecretProtector CreateProtector()
    {
        var keyProvider = new EncryptionKeyProvider();
        keyProvider.Initialize(RandomNumberGenerator.GetBytes(32));
        return new SecretProtector(keyProvider);
    }
}
