using Renci.SshNet;
using SporeSync.Domain.Models;

namespace SporeSync.Infrastructure.Configuration;

public class SettingsOptions
{
    public RemotePathOptions RemotePath { get; set; } = new();
    public RemoteMonitorOptions RemoteMonitor { get; set; } = new();
    public SshClientOptions SshConfiguration { get; set; } = new();
}

