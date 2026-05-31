using SporeSync.Domain.Model;

namespace SporeSync.Domain.Interface;

public interface ISystemPropertyRepository
{
    Task<SystemProperty?> GetByNameAsync(string propertyName, CancellationToken cancellationToken = default);

    Task<SystemProperty> UpsertAsync(string propertyName, string propertyValue, CancellationToken cancellationToken = default);

    Task<SystemProperty> InsertIfMissingAsync(
        string propertyName,
        string propertyValue,
        CancellationToken cancellationToken = default);
}
