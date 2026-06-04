namespace SporeSync.Business;

public sealed class SporeSyncOptions
{
    public const string SectionName = "SporeSync";

    public string DestinationRootPath { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "downloads");

    public int SchedulerIntervalSeconds { get; set; } = 10;

    public int DownloadPollIntervalMs { get; set; } = 1000;

    public int SftpConnectionTimeoutSeconds { get; set; } = 30;

    public int SftpOperationTimeoutSeconds { get; set; } = 300;
}
