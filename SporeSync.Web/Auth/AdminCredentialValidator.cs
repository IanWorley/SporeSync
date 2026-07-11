using System.Security.Cryptography;
using System.Text;

namespace SporeSync.Web.Auth;

/// <summary>
/// Validates login attempts against the configured single admin credential.
/// </summary>
public sealed class AdminCredentialValidator
{
    private readonly AuthOptions _options;

    public AdminCredentialValidator(AuthOptions options)
    {
        _options = options;
    }

    public bool Validate(string? username, string? password)
    {
        if (!_options.Enabled || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            return false;
        }

        var usernameMatches = FixedTimeEquals(username, _options.Username);

        // Always run the password check so response timing does not reveal
        // whether the username was correct.
        var passwordMatches = !string.IsNullOrEmpty(_options.PasswordHash)
            ? PasswordHasher.Verify(password, _options.PasswordHash)
            : FixedTimeEquals(password, _options.Password);

        return usernameMatches && passwordMatches;
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);

        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
