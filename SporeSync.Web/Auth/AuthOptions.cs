namespace SporeSync.Web.Auth;

/// <summary>
/// Configuration for the single-admin authentication scheme, bound from the "Auth" section.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Whether login is required for API, SPA data, and SignalR access.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The single admin username.</summary>
    public string Username { get; set; } = "admin";

    /// <summary>
    /// Plaintext admin password. Intended for local development only;
    /// prefer <see cref="PasswordHash"/> in real deployments.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// PBKDF2 password hash produced by <c>dotnet run --project SporeSync.Web -- hash-password</c>.
    /// Takes precedence over <see cref="Password"/> when both are set.
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Sliding session lifetime for the auth cookie, in hours.</summary>
    public double SessionHours { get; set; } = 12;

    public void Validate()
    {
        if (!Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            throw new InvalidOperationException(
                "Authentication is enabled but Auth:Username is empty. " +
                "Set Auth:Username (environment variable Auth__Username) or disable authentication with Auth:Enabled=false.");
        }

        if (string.IsNullOrEmpty(Password) && string.IsNullOrEmpty(PasswordHash))
        {
            throw new InvalidOperationException(
                "Authentication is enabled but no admin credential is configured. " +
                "Set Auth:PasswordHash (environment variable Auth__PasswordHash) to a hash generated with " +
                "'dotnet run --project SporeSync.Web -- hash-password', set Auth:Password for local development, " +
                "or disable authentication with Auth:Enabled=false.");
        }

        if (!string.IsNullOrEmpty(PasswordHash) && !PasswordHasher.IsValidHashFormat(PasswordHash))
        {
            throw new InvalidOperationException(
                "Auth:PasswordHash is not in the expected format. Generate a value with " +
                "'dotnet run --project SporeSync.Web -- hash-password'.");
        }

        if (SessionHours <= 0)
        {
            throw new InvalidOperationException("Auth:SessionHours must be greater than zero.");
        }
    }
}
