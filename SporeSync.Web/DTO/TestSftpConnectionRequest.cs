using System.ComponentModel.DataAnnotations;

namespace SporeSync.Web.DTO;

public sealed record TestSftpConnectionRequest(
    Guid? ProfileId,
    [param: Required, MaxLength(255)] string Host,
    [param: Range(1, 65535)] int Port,
    [param: Required, MaxLength(200)] string Username,
    string? Password,
    string? PrivateKey,
    string? PrivateKeyPassphrase,
    [param: MaxLength(100)] string? HostKeyFingerprintSha256,
    [param: MaxLength(2000)] string? SourcePath);
