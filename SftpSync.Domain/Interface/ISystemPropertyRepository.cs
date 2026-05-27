using SftpSync.Domain.Model;

namespace SftpSync.Domain.Interface;

public interface ISystemPropertyRepository
{
    Task<SystemProperty?> GetByNameAsync(string propertyName, CancellationToken cancellationToken = default);

    Task<SystemProperty> UpsertAsync(string propertyName, string propertyValue, CancellationToken cancellationToken = default);
}
