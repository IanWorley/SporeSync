using SporeSync.Web.Auth;

namespace SporeSync.Business.Tests;

public sealed class AuthOptionsTests
{
    [Fact]
    public void Validate_Throws_WhenEnabledWithoutAnyCredential()
    {
        var options = new AuthOptions { Enabled = true, Username = "admin" };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains("Auth:PasswordHash", exception.Message);
    }

    [Fact]
    public void Validate_Throws_WhenEnabledWithoutUsername()
    {
        var options = new AuthOptions { Enabled = true, Username = " ", Password = "s3cret" };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains("Auth:Username", exception.Message);
    }

    [Fact]
    public void Validate_Throws_ForMalformedPasswordHash()
    {
        var options = new AuthOptions { Enabled = true, PasswordHash = "not-a-valid-hash" };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains("Auth:PasswordHash", exception.Message);
    }

    [Fact]
    public void Validate_Throws_ForNonPositiveSessionHours()
    {
        var options = new AuthOptions { Enabled = true, Password = "s3cret", SessionHours = 0 };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains("Auth:SessionHours", exception.Message);
    }

    [Fact]
    public void Validate_Succeeds_WhenDisabled_WithoutCredentials()
    {
        var options = new AuthOptions { Enabled = false };

        options.Validate();
    }

    [Fact]
    public void Validate_Succeeds_WithPlaintextPassword()
    {
        var options = new AuthOptions { Enabled = true, Password = "s3cret" };

        options.Validate();
    }

    [Fact]
    public void Validate_Succeeds_WithGeneratedPasswordHash()
    {
        var options = new AuthOptions { Enabled = true, PasswordHash = PasswordHasher.Hash("s3cret") };

        options.Validate();
    }
}
