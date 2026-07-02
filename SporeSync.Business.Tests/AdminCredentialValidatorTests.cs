using SporeSync.Web.Auth;

namespace SporeSync.Business.Tests;

public sealed class AdminCredentialValidatorTests
{
    [Fact]
    public void Validate_ReturnsTrue_ForCorrectPlaintextCredentials()
    {
        var validator = new AdminCredentialValidator(new AuthOptions
        {
            Enabled = true,
            Username = "admin",
            Password = "s3cret"
        });

        Assert.True(validator.Validate("admin", "s3cret"));
    }

    [Fact]
    public void Validate_ReturnsTrue_ForCorrectHashedCredentials()
    {
        var validator = new AdminCredentialValidator(new AuthOptions
        {
            Enabled = true,
            Username = "admin",
            PasswordHash = PasswordHasher.Hash("s3cret")
        });

        Assert.True(validator.Validate("admin", "s3cret"));
    }

    [Fact]
    public void Validate_PrefersHash_WhenBothPasswordAndHashAreSet()
    {
        var validator = new AdminCredentialValidator(new AuthOptions
        {
            Enabled = true,
            Username = "admin",
            Password = "plaintext-password",
            PasswordHash = PasswordHasher.Hash("hashed-password")
        });

        Assert.True(validator.Validate("admin", "hashed-password"));
        Assert.False(validator.Validate("admin", "plaintext-password"));
    }

    [Theory]
    [InlineData("admin", "wrong")]
    [InlineData("root", "s3cret")]
    [InlineData("", "s3cret")]
    [InlineData("admin", "")]
    [InlineData(null, "s3cret")]
    [InlineData("admin", null)]
    public void Validate_ReturnsFalse_ForIncorrectOrMissingCredentials(string? username, string? password)
    {
        var validator = new AdminCredentialValidator(new AuthOptions
        {
            Enabled = true,
            Username = "admin",
            Password = "s3cret"
        });

        Assert.False(validator.Validate(username, password));
    }

    [Fact]
    public void Validate_ReturnsFalse_WhenAuthDisabled()
    {
        var validator = new AdminCredentialValidator(new AuthOptions
        {
            Enabled = false,
            Username = "admin",
            Password = "s3cret"
        });

        Assert.False(validator.Validate("admin", "s3cret"));
    }
}
