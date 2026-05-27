namespace SftpSync.Web.DTO;

public sealed record SftpConnectionProfileResponse(
    Guid Id,
    string Name,
    string Host,
    int Port,
    string Username,
    bool HasPassword,
    bool HasPrivateKey,
    bool HasPrivateKeyPassphrase,
    bool IsDefault);
