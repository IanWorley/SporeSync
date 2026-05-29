namespace SftpSync.Web.DTO;

public sealed record SystemPropertyResponse(
    Guid Id,
    string PropertyName,
    string PropertyValue);
