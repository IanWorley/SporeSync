using System.Security.Cryptography;
using System.Text;
using SftpSync.Business.Interface;

namespace SftpSync.Business.Service;

public sealed class SecretProtector : ISecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly IEncryptionKeyProvider _keyProvider;

    public SecretProtector(IEncryptionKeyProvider keyProvider)
    {
        _keyProvider = keyProvider;
    }

    public string Protect(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_keyProvider.GetKey(), TagSize);
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

        using var aes = new AesGcm(_keyProvider.GetKey(), TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintextBytes);

        return Encoding.UTF8.GetString(plaintextBytes);
    }
}
