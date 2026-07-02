using System.ComponentModel.DataAnnotations;

namespace SporeSync.Web.DTO;

public sealed record ScanHostKeyRequest(
    [param: Required]
    [param: MaxLength(255)]
    string Host,

    [param: Range(1, 65535)]
    int Port = 22);
