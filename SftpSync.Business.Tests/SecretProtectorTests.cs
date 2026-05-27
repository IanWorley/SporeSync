using System.Security.Cryptography;
using SftpSync.Business.Service;

namespace SftpSync.Business.Tests;

public sealed class SecretProtectorTests : IDisposable
{
    private const string EnvironmentVariableName = "SFTPSYNC_SECRET_KEY";
    private readonly string? _originalSecretKey = Environment.GetEnvironmentVariable(EnvironmentVariableName);

    [Fact]
    public void ProtectAndUnprotect_RoundTripSecret_WithBase64Key()
    {
        Environment.SetEnvironmentVariable(
            EnvironmentVariableName,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var protector = new SecretProtector();

        var protectedValue = protector.Protect("secret-password");
        var unprotectedValue = protector.Unprotect(protectedValue);

        Assert.Equal("secret-password", unprotectedValue);
        Assert.NotEqual("secret-password", protectedValue);
        Assert.StartsWith("v1:", protectedValue);
    }

    [Fact]
    public void ProtectAndUnprotect_RoundTripSecret_WithPlainTextKey()
    {
        Environment.SetEnvironmentVariable(EnvironmentVariableName, "development-secret");
        var protector = new SecretProtector();

        var protectedValue = protector.Protect("private-key");

        Assert.Equal("private-key", protector.Unprotect(protectedValue));
    }

    [Fact]
    public void Protect_Throws_WhenSecretKeyIsMissing()
    {
        Environment.SetEnvironmentVariable(EnvironmentVariableName, null);
        var protector = new SecretProtector();

        var exception = Assert.Throws<InvalidOperationException>(() => protector.Protect("secret"));

        Assert.Equal(
            "Environment variable 'SFTPSYNC_SECRET_KEY' must be set before storing or reading SFTP secrets.",
            exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-protected")]
    [InlineData("v2:a:b:c")]
    [InlineData("v1:a:b")]
    public void Unprotect_Throws_WhenProtectedValueHasUnsupportedFormat(string protectedValue)
    {
        Environment.SetEnvironmentVariable(EnvironmentVariableName, "development-secret");
        var protector = new SecretProtector();

        var exception = Assert.Throws<InvalidOperationException>(() => protector.Unprotect(protectedValue));

        Assert.Equal("Secret value is not in a supported protected format.", exception.Message);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvironmentVariableName, _originalSecretKey);
    }
}
