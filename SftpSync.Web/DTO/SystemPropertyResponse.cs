namespace SftpSync.Web.DTO;

public sealed record SystemPropertyResponse(
    string Id,
    string PropertyName,
    string PropertyValue);
