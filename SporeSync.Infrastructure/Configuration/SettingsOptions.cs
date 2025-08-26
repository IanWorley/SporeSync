using Renci.SshNet;
using SporeSync.Domain.Models;

namespace SporeSync.Infrastructure.Configuration;

public class SettingsOptions
{
    public PathOptions PathOptions { get; set; } = new();
    public MonitorOptions MonitorOptions { get; set; } = new();
    public SshClientOptions SshConfiguration { get; set; } = new();
}

