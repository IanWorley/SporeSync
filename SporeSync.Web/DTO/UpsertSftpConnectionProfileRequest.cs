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

    string? Password,

    string? PrivateKey,

    string? PrivateKeyPassphrase,

    bool IsDefault = true);