namespace SporeSync.Domain.Model;

public sealed class UpsertSftpConnectionProfile
{
    public Guid? Id { get; init; }

    public required string Name { get; init; }

    public required string Host { get; init; }

    public int Port { get; init; } = 22;

    public required string Username { get; init; }

    /// <summary>
    /// Selects the only credential type that will remain stored after this upsert.
    /// </summary>
    public SftpAuthenticationMethod AuthenticationMethod { get; init; }

    public string? Password { get; init; }

    public string? PrivateKey { get; init; }

    public string? PrivateKeyPassphrase { get; init; }

    /// <summary>
    /// Explicitly clears a stored private key passphrase. When false, a blank
    /// replacement preserves the existing passphrase.
    /// </summary>
    public bool RemovePrivateKeyPassphrase { get; init; }

    /// <summary>
    /// SSH host key fingerprint to pin. Null preserves the currently stored fingerprint,
    /// an empty/whitespace value clears the pin (re-enabling trust-on-first-use), and a
    /// non-blank value replaces the pin after normalization.
    /// </summary>
    public string? HostKeyFingerprintSha256 { get; init; }

    public bool IsDefault { get; init; } = true;
}
