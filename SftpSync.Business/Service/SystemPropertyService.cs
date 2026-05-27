using SftpSync.Business.Interface;
using SftpSync.Domain.Interface;
using SftpSync.Domain.Model;

namespace SftpSync.Business.Service;

public sealed class SystemPropertyService : ISystemPropertyService
{
    private readonly ISystemPropertyRepository _systemPropertyRepository;

    public SystemPropertyService(ISystemPropertyRepository systemPropertyRepository)
    {
        _systemPropertyRepository = systemPropertyRepository;
    }

    public Task<SystemProperty?> GetByNameAsync(string propertyName, CancellationToken cancellationToken = default)
    {
        return _systemPropertyRepository.GetByNameAsync(propertyName, cancellationToken);
    }

    public Task<SystemProperty> UpsertAsync(
        string propertyName,
        string propertyValue,
        CancellationToken cancellationToken = default)
    {
        return _systemPropertyRepository.UpsertAsync(propertyName, propertyValue, cancellationToken);
    }
}
