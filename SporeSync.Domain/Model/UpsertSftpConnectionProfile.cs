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
    /// Trusted SSH host key fingerprints. Null preserves the currently stored collection;
    /// an empty collection clears it, causing all connections to fail closed.
    /// </summary>
    public IReadOnlyList<string>? TrustedHostKeyFingerprintsSha256 { get; init; }

    public bool IsDefault { get; init; } = true;
}
