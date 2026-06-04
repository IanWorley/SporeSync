using SporeSync.Domain.Model;

namespace SporeSync.Business.Interface;

public interface ISystemPropertyService
{
    Task<SystemProperty?> GetByNameAsync(string propertyName, CancellationToken cancellationToken = default);

    Task<SystemProperty> UpsertAsync(string propertyName, string propertyValue, CancellationToken cancellationToken = default);
}
