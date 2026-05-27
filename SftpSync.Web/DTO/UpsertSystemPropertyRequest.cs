using System.ComponentModel.DataAnnotations;

namespace SftpSync.Web.DTO;

public sealed record UpsertSystemPropertyRequest(
    [property: Required]
    [property: MaxLength(1000)]
    string PropertyValue);
