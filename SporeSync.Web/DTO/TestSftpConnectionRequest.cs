using System.ComponentModel.DataAnnotations;

namespace SporeSync.Web.DTO;

public sealed record TestSftpConnectionRequest(
    Guid? ProfileId,
    [param: Required, MaxLength(255)] string Host,
    [param: Range(1, 65535)] int Port,
    [param: Required, MaxLength(200)] string Username,
    [param: Required] string AuthenticationMethod,
    string? Password,
    string? PrivateKey,
    string? PrivateKeyPassphrase,
    bool RemovePrivateKeyPassphrase,
    [param: MaxLength(100)] string? HostKeyFingerprintSha256,
    IReadOnlyList<string>? TrustedHostKeyFingerprintsSha256,
    [param: MaxLength(2000)] string? SourcePath);
