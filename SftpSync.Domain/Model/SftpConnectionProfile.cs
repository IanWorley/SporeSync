namespace SftpSync.Domain.Model;

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

    public bool IsDefault { get; init; }
}
