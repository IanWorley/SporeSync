namespace SporeSync.Web.DTO;

public sealed record HostKeyScanResponse(
    string HostKeyAlgorithm,
    int KeyLength,
    string FingerprintSha256);
