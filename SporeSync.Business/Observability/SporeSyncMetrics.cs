using System.Diagnostics.Metrics;

namespace SporeSync.Business.Observability;

/// <summary>
/// Application metrics for the sync pipeline, exposed through the standard
/// System.Diagnostics.Metrics API so they can be collected with
/// OpenTelemetry, dotnet-counters, or any other .NET metrics listener.
/// </summary>
public sealed class SporeSyncMetrics : IDisposable
{
    public const string MeterName = "SporeSync";

    private readonly Meter _meter;
    private readonly Counter<long> _scansCompleted;
    private readonly Counter<long> _scansFailed;
    private readonly Counter<long> _filesEnqueued;
    private readonly Counter<long> _downloadsCompleted;
    private readonly Counter<long> _downloadsFailed;
    private readonly Counter<long> _bytesDownloaded;
    private readonly Counter<long> _runsPruned;
    private readonly Counter<long> _queueItemsPruned;
    private readonly Histogram<double> _scanDurationSeconds;

    public SporeSyncMetrics()
    {
        _meter = new Meter(MeterName);
        _scansCompleted = _meter.CreateCounter<long>(
            "sporesync.scans.completed",
            description: "Number of successfully completed remote scans.");
        _scansFailed = _meter.CreateCounter<long>(
            "sporesync.scans.failed",
            description: "Number of remote scans that ended in failure.");
        _filesEnqueued = _meter.CreateCounter<long>(
            "sporesync.queue.enqueued",
            description: "Number of visible queue entries enqueued by scans.");
        _downloadsCompleted = _meter.CreateCounter<long>(
            "sporesync.downloads.completed",
            description: "Number of successfully downloaded files.");
        _downloadsFailed = _meter.CreateCounter<long>(
            "sporesync.downloads.failed",
            description: "Number of failed file downloads.");
        _bytesDownloaded = _meter.CreateCounter<long>(
            "sporesync.downloads.bytes",
            unit: "By",
            description: "Total bytes downloaded from SFTP sources.");
        _runsPruned = _meter.CreateCounter<long>(
            "sporesync.retention.runs_pruned",
            description: "Number of historical sync runs removed by retention pruning.");
        _queueItemsPruned = _meter.CreateCounter<long>(
            "sporesync.retention.queue_items_pruned",
            description: "Number of stale queue items removed by retention pruning.");
        _scanDurationSeconds = _meter.CreateHistogram<double>(
            "sporesync.scan.duration",
            unit: "s",
            description: "Duration of remote scans.");
    }

    public void RecordScanCompleted(double durationSeconds, int enqueuedCount)
    {
        _scansCompleted.Add(1);
        _scanDurationSeconds.Record(durationSeconds);
        if (enqueuedCount > 0)
        {
            _filesEnqueued.Add(enqueuedCount);
        }
    }

    public void RecordScanFailed(double durationSeconds)
    {
        _scansFailed.Add(1);
        _scanDurationSeconds.Record(durationSeconds);
    }

    public void RecordDownloadCompleted(long bytesDownloaded)
    {
        _downloadsCompleted.Add(1);
        if (bytesDownloaded > 0)
        {
            _bytesDownloaded.Add(bytesDownloaded);
        }
    }

    public void RecordDownloadFailed()
    {
        _downloadsFailed.Add(1);
    }

    public void RecordRetentionPruned(int runsPruned, int queueItemsPruned)
    {
        if (runsPruned > 0)
        {
            _runsPruned.Add(runsPruned);
        }

        if (queueItemsPruned > 0)
        {
            _queueItemsPruned.Add(queueItemsPruned);
        }
    }

    public void Dispose() => _meter.Dispose();
}
