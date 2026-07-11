namespace SporeSync.Web.DTO;

public sealed record SftpConnectionProfileResponse(
    Guid Id,
    string Name,
    string Host,
    int Port,
    string Username,
    string AuthenticationMethod,
    bool HasPassword,
    bool HasPrivateKey,
    bool HasPrivateKeyPassphrase,
    IReadOnlyList<string> TrustedHostKeyFingerprintsSha256,
    bool IsDefault);
