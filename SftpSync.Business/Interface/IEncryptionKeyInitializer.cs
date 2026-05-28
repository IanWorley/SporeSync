namespace SftpSync.Business.Interface;

public interface IEncryptionKeyInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
