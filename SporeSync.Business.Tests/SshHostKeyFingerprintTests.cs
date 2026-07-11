using SporeSync.Business.Sftp;

namespace SporeSync.Business.Tests;

public sealed class SshHostKeyFingerprintTests
{
    // ssh-keygen style unpadded base64 of a 32-byte SHA-256 digest.
    private const string Digest = "nThbg6kXUpJWGl7E1IGOCspRomTxdCARLviKw6E5SY8";
    private const string Canonical = $"SHA256:{Digest}";

    [Theory]
    [InlineData(Canonical)]
    [InlineData(Digest)]
    [InlineData($"sha256:{Digest}")]
    [InlineData($"  {Canonical}  ")]
    [InlineData($"{Digest}=")]
    public void Normalize_ReturnsCanonicalForm(string input)
    {
        Assert.Equal(Canonical, SshHostKeyFingerprint.Normalize(input));
    }

    [Theory]
    [InlineData("not-base64!!!")]
    [InlineData("SHA256:short")]
    [InlineData("SHA256:aGVsbG8")]
    [InlineData("16:27:ac:a5:76:28:2d:36:63:1b:56:4d:eb:df:a6:48")]
    public void Normalize_Throws_ForInvalidFingerprints(string input)
    {
        Assert.Throws<FormatException>(() => SshHostKeyFingerprint.Normalize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_Throws_ForBlankInput(string input)
    {
        Assert.Throws<ArgumentException>(() => SshHostKeyFingerprint.Normalize(input));
    }

    [Theory]
    [InlineData(Canonical, Digest)]
    [InlineData(Digest, Canonical)]
    [InlineData($"sha256:{Digest}", $"{Digest}=")]
    public void Matches_ReturnsTrue_ForEquivalentRepresentations(string pinned, string presented)
    {
        Assert.True(SshHostKeyFingerprint.Matches(pinned, presented));
    }

    [Fact]
    public void Matches_ReturnsFalse_ForDifferentDigests()
    {
        var other = "SHA256:" + Convert.ToBase64String(new byte[32]).TrimEnd('=');

        Assert.False(SshHostKeyFingerprint.Matches(Canonical, other));
    }

    [Theory]
    [InlineData("", Canonical)]
    [InlineData(Canonical, "")]
    [InlineData("garbage", Canonical)]
    [InlineData(Canonical, "garbage")]
    public void Matches_ReturnsFalse_ForBlankOrInvalidInput(string pinned, string presented)
    {
        Assert.False(SshHostKeyFingerprint.Matches(pinned, presented));
    }
}
