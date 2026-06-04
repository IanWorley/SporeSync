using SporeSync.Business.Interface;

namespace SporeSync.Business.Service;

public sealed class EncryptionKeyProvider : IEncryptionKeyProvider
{
    public const string CurrentVersion = "v1";
    private byte[]? _key;

    public bool IsInitialized => _key is not null;

    public string Version => CurrentVersion;

    public byte[] GetKey()
    {
        if (_key is null)
        {
            throw new InvalidOperationException("Encryption key has not been initialized.");
        }

        return (byte[])_key.Clone();
    }

    public void Initialize(byte[] key)
    {
        if (key.Length != 32)
        {
            throw new InvalidOperationException("Encryption key must be exactly 32 bytes.");
        }

        _key = (byte[])key.Clone();
    }
}
