namespace SftpSync.Business;

public sealed class SftpSyncOptions
{
    public const string SectionName = "SftpSync";

    public int SchedulerIntervalSeconds { get; set; } = 10;

    public int DownloadPollIntervalMs { get; set; } = 1000;

    public int SftpConnectionTimeoutSeconds { get; set; } = 30;

    public int SftpOperationTimeoutSeconds { get; set; } = 300;
}
