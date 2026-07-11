namespace SporeSync.Business.Sftp;

/// <summary>
/// Canonical handling of SSH host key SHA-256 fingerprints. The canonical form is
/// "SHA256:&lt;unpadded base64&gt;", matching the output of OpenSSH and `ssh-keygen -lf`.
/// </summary>
public static class SshHostKeyFingerprint
{
    private const string Prefix = "SHA256:";
    private const int Sha256ByteLength = 32;

    /// <summary>
    /// Normalizes a user- or server-supplied fingerprint to canonical form.
    /// Accepts values with or without the "SHA256:" prefix and with or without base64 padding.
    /// </summary>
    /// <exception cref="FormatException">The value is not a valid SHA-256 fingerprint.</exception>
    public static string Normalize(string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);

        var value = fingerprint.Trim();
        if (value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value[Prefix.Length..];
        }

        value = value.TrimEnd('=');

        byte[] digest;
        try
        {
            digest = Convert.FromBase64String(Pad(value));
        }
        catch (FormatException)
        {
            throw new FormatException(
                "Host key fingerprint must be a base64-encoded SHA-256 digest, " +
                "for example 'SHA256:nThbg6kXUpJWGl7E1IGOCspRomTxdCARLviKw6E5SY8'.");
        }

        if (digest.Length != Sha256ByteLength)
        {
            throw new FormatException(
                $"Host key fingerprint must decode to {Sha256ByteLength} bytes but decoded to {digest.Length}. " +
                "Only SHA-256 fingerprints are supported.");
        }

        return Prefix + value;
    }

    /// <summary>
    /// Compares a pinned fingerprint against the fingerprint presented by a server.
    /// Both values are normalized before comparison; invalid values never match.
    /// </summary>
    public static bool Matches(string pinnedFingerprint, string presentedFingerprint)
    {
        if (string.IsNullOrWhiteSpace(pinnedFingerprint) || string.IsNullOrWhiteSpace(presentedFingerprint))
        {
            return false;
        }

        try
        {
            return string.Equals(Normalize(pinnedFingerprint), Normalize(presentedFingerprint), StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Pad(string base64)
    {
        var remainder = base64.Length % 4;
        return remainder == 0 ? base64 : base64 + new string('=', 4 - remainder);
    }
}
