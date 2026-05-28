namespace SftpSync.Web.DTO;

public sealed record StatusResponse(
    string Status,
    string Environment,
    DateTimeOffset CurrentTime,
    bool DatabaseAvailable,
    bool EncryptionKeyInitialized,
    string EncryptionKeyVersion);
