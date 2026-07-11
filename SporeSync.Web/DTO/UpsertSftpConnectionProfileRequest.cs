using System.ComponentModel.DataAnnotations;

namespace SporeSync.Web.DTO;

public sealed record UpsertSftpConnectionProfileRequest(
    [param: Required]
    [param: MaxLength(200)]
    string Name,

    [param: Required]
    [param: MaxLength(255)]
    string Host,

    [param: Range(1, 65535)]
    int Port,

    [param: Required]
    [param: MaxLength(200)]
    string Username,

    [param: Required]
    string AuthenticationMethod,

    string? Password,

    string? PrivateKey,

    string? PrivateKeyPassphrase,

    bool RemovePrivateKeyPassphrase = false,

    IReadOnlyList<string>? TrustedHostKeyFingerprintsSha256 = null,

    bool IsDefault = true);
