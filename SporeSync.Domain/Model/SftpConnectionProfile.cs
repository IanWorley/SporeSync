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
    /// Trusted SSH host key fingerprints in canonical "SHA256:&lt;base64&gt;" form.
    /// Connections are rejected unless the server presents one of these keys.
    /// </summary>
    public IReadOnlyList<string> TrustedHostKeyFingerprintsSha256 { get; set; } = [];

    public bool IsDefault { get; init; }
}
