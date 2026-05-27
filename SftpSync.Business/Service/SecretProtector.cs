using System.Security.Cryptography;
using System.Text;
using SftpSync.Business.Interface;

namespace SftpSync.Business.Service;

public sealed class SecretProtector : ISecretProtector
{
    private const string EnvironmentVariableName = "SFTPSYNC_SECRET_KEY";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public string Protect(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(GetKey(), TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        return string.Join(
            ':',
            "v1",
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(tag),
            Convert.ToBase64String(ciphertext));
    }

    public string Unprotect(string protectedValue)
    {
        var parts = protectedValue.Split(':');
        if (parts.Length != 4 || parts[0] != "v1")
        {
            throw new InvalidOperationException("Secret value is not in a supported protected format.");
        }

        var nonce = Convert.FromBase64String(parts[1]);
        var tag = Convert.FromBase64String(parts[2]);
        var ciphertext = Convert.FromBase64String(parts[3]);
        var plaintextBytes = new byte[ciphertext.Length];

        using var aes = new AesGcm(GetKey(), TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintextBytes);

        return Encoding.UTF8.GetString(plaintextBytes);
    }

    private static byte[] GetKey()
    {
        var configuredKey = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            throw new InvalidOperationException(
                $"Environment variable '{EnvironmentVariableName}' must be set before storing or reading SFTP secrets.");
        }

        return TryReadBase64Key(configuredKey, out var key)
            ? key
            : SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey));
    }

    private static bool TryReadBase64Key(string configuredKey, out byte[] key)
    {
        try
        {
            key = Convert.FromBase64String(configuredKey);
            return key.Length == 32;
        }
        catch (FormatException)
        {
            key = [];
            return false;
        }
    }
}
