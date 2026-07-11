namespace SporeSync.Domain.Model;

public sealed class SftpConnectionProfile
{
    public Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Host { get; init; }

    public int Port { get; init; }

    public required string Username { get; init; }

    public string? EncryptedPassword { get; init; }

    public string? EncryptedPrivateKey { get; init; }

    public string? EncryptedPrivateKeyPassphrase { get; init; }

    /// <summary>
    /// Pinned SSH host key fingerprint in canonical "SHA256:&lt;base64&gt;" form.
    /// When set, connections are rejected unless the server presents a matching key.
    /// When null, the fingerprint is captured and pinned on first use.
    /// </summary>
    public string? HostKeyFingerprintSha256 { get; init; }

    public bool IsDefault { get; init; }
}
