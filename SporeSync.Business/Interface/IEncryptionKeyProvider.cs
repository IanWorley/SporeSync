namespace SporeSync.Business.Interface;

public interface IEncryptionKeyProvider
{
    bool IsInitialized { get; }

    string Version { get; }

    byte[] GetKey();

    void Initialize(byte[] key);
}
