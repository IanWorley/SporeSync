using SftpSync.Domain.Model;

namespace SftpSync.Business.Interface;

public interface ISftpSyncJobService
{
    IReadOnlyCollection<SftpSyncJob> GetConfiguredJobs();
}
