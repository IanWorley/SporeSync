using SftpSync.Business.Interface;
using SftpSync.Domain.Model;

namespace SftpSync.Business.Service;

public sealed class SftpSyncJobService : ISftpSyncJobService
{
    public IReadOnlyCollection<SftpSyncJob> GetConfiguredJobs()
    {
        return
        [
            new SftpSyncJob
            {
                Id = Guid.Parse("680fb417-43e4-4c0a-bd43-968b0fe97bdb"),
                Name = "Sample SFTP Sync Job",
                SourcePath = "/remote/source",
                DestinationPath = "/local/destination",
                IsEnabled = false
            }
        ];
    }
}
