public class RemoteMonitorOptions
{
    public int CheckIntervalSeconds { get; set; } = 30;
    public int ErrorRetryDelaySeconds { get; set; } = 60;
    public int MaxRetryAttempts { get; set; } = 3;
    public bool EnableLogging { get; set; } = true;
}