namespace SftpSync.Domain.Model;

public sealed class UpsertSftpConnectionProfile
{
    public Guid? Id { get; init; }

    public required string Name { get; init; }

    public required string Host { get; init; }

    public int Port { get; init; } = 22;

    public required string Username { get; init; }

    public string? Password { get; init; }

    public string? PrivateKey { get; init; }

    public string? PrivateKeyPassphrase { get; init; }

    public bool IsDefault { get; init; } = true;
}
