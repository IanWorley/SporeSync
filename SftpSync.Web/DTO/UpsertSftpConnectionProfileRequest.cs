using System.ComponentModel.DataAnnotations;

namespace SftpSync.Web.DTO;

public sealed record UpsertSftpConnectionProfileRequest(
    [property: Required]
    [property: MaxLength(200)]
    string Name,

    [property: Required]
    [property: MaxLength(255)]
    string Host,

    [property: Range(1, 65535)]
    int Port,

    [property: Required]
    [property: MaxLength(200)]
    string Username,

    string? Password,

    string? PrivateKey,

    string? PrivateKeyPassphrase,

    bool IsDefault = true);
