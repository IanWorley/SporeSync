using SporeSync.Web.Auth;

namespace SporeSync.Business.Tests;

public sealed class PasswordHasherTests
{
    [Fact]
    public void Verify_ReturnsTrue_ForCorrectPassword()
    {
        var hash = PasswordHasher.Hash("correct horse battery staple");

        Assert.True(PasswordHasher.Verify("correct horse battery staple", hash));
    }

    [Fact]
    public void Verify_ReturnsFalse_ForWrongPassword()
    {
        var hash = PasswordHasher.Hash("correct horse battery staple");

        Assert.False(PasswordHasher.Verify("wrong password", hash));
    }

    [Fact]
    public void Hash_ProducesUniqueSaltedHashes()
    {
        var first = PasswordHasher.Hash("password");
        var second = PasswordHasher.Hash("password");

        Assert.NotEqual(first, second);
        Assert.True(PasswordHasher.Verify("password", first));
        Assert.True(PasswordHasher.Verify("password", second));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("PBKDF2-SHA256.abc.def.ghi")]
    [InlineData("PBKDF2-SHA256.0.c2FsdA==.aGFzaA==")]
    [InlineData("PBKDF2-SHA1.10000.c2FsdA==.aGFzaA==")]
    [InlineData("PBKDF2-SHA256.10000.%%%.aGFzaA==")]
    public void Verify_ReturnsFalse_ForMalformedStoredHash(string storedHash)
    {
        Assert.False(PasswordHasher.Verify("password", storedHash));
        Assert.False(PasswordHasher.IsValidHashFormat(storedHash));
    }

    [Fact]
    public void Verify_ReturnsFalse_ForEmptyPassword()
    {
        var hash = PasswordHasher.Hash("password");

        Assert.False(PasswordHasher.Verify("", hash));
    }

    [Fact]
    public void IsValidHashFormat_ReturnsTrue_ForGeneratedHash()
    {
        Assert.True(PasswordHasher.IsValidHashFormat(PasswordHasher.Hash("password")));
    }

    [Fact]
    public void Hash_Throws_ForEmptyPassword()
    {
        Assert.Throws<ArgumentException>(() => PasswordHasher.Hash(""));
    }
}
