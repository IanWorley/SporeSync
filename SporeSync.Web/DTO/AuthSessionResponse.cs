namespace SporeSync.Web.DTO;

public sealed record AuthSessionResponse(
    bool AuthRequired,
    bool Authenticated,
    string? Username);
