using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace SporeSync.Business.Worker;

public sealed record ManualRunWorkItem(Guid JobId, Guid RunId);

public interface IManualRunQueue
{
    bool TryReserve(out ManualRunQueueReservation? reservation);
}

public sealed class ManualRunQueueReservation : IDisposable
{
    private ManualRunQueue? _queue;

    internal ManualRunQueueReservation(ManualRunQueue queue) => _queue = queue;

    public void Enqueue(ManualRunWorkItem item)
    {
        var queue = Interlocked.Exchange(ref _queue, null)
            ?? throw new InvalidOperationException("The queue reservation has already been used.");
        queue.EnqueueReserved(item);
    }

    public void Dispose() => Interlocked.Exchange(ref _queue, null)?.ReleaseReservation();
}

public sealed class ManualRunQueue : IManualRunQueue
{
    private readonly Channel<ManualRunWorkItem> _channel;
    private readonly SemaphoreSlim _availableSlots;

    public ManualRunQueue(IOptions<SporeSyncOptions> options)
    {
        var capacity = options.Value.ManualRunQueueCapacity;
        _channel = Channel.CreateBounded<ManualRunWorkItem>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });
        _availableSlots = new SemaphoreSlim(capacity, capacity);
    }

    public bool TryReserve(out ManualRunQueueReservation? reservation)
    {
        if (!_availableSlots.Wait(0))
        {
            reservation = null;
            return false;
        }

        reservation = new ManualRunQueueReservation(this);
        return true;
    }

    internal async ValueTask<ManualRunWorkItem> ReadAsync(CancellationToken cancellationToken)
    {
        var item = await _channel.Reader.ReadAsync(cancellationToken);
        _availableSlots.Release();
        return item;
    }

    internal bool TryRead(out ManualRunWorkItem? item)
    {
        if (!_channel.Reader.TryRead(out item))
        {
            return false;
        }

        _availableSlots.Release();
        return true;
    }

    internal void EnqueueReserved(ManualRunWorkItem item)
    {
        if (!_channel.Writer.TryWrite(item))
        {
            _availableSlots.Release();
            throw new InvalidOperationException("A reserved manual-run queue slot could not be committed.");
        }
    }

    internal void ReleaseReservation() => _availableSlots.Release();
}
