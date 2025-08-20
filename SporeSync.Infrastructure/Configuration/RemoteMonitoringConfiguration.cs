using SporeSync.Domain.Models;

namespace SporeSync.Infrastructure.Configuration;

public class RemotePathConfig
{
    public string RemotePath { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public SshConfiguration SshConfiguration { get; set; } = new();
    public int CheckIntervalSeconds { get; set; } = 30;
}

public class RemoteMonitorOptions
{
    public int CheckIntervalSeconds { get; set; } = 30;
    public int ErrorRetryDelaySeconds { get; set; } = 60;
}
