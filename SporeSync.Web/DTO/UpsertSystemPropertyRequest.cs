using System.ComponentModel.DataAnnotations;

namespace SporeSync.Web.DTO;

public sealed record UpsertSystemPropertyRequest(
    [param: Required]
    [param: MaxLength(1000)]
    string PropertyValue);
