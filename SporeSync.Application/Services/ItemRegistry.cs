using System.Collections;
using System.Collections.Concurrent;
using SporeSync.Domain.Models;

namespace SporeSync.Application;

public class ItemRegistry : IEnumerable<TrackedItem>
{

    private readonly ConcurrentDictionary<string, TrackedItem> _trackedFiles;

    public ItemRegistry()
    {
        _trackedFiles = new ConcurrentDictionary<string, TrackedItem>();
    }

    public void Add(TrackedItem item)
    {
        _trackedFiles.AddOrUpdate(item.RemotePath, item, (key, existing) => item);

    }

    public void Remove(TrackedItem item)
    {
        _trackedFiles.TryRemove(item.RemotePath, out _);
    }

    public IEnumerator<TrackedItem> GetEnumerator()
    {
        return _trackedFiles.Values.GetEnumerator();
    }

    public TrackedItem? Get(string remotePath)
    {
        return _trackedFiles.TryGetValue(remotePath, out var item) ? item : null;
    }

    public bool Contains(string remotePath)
    {
        return _trackedFiles.ContainsKey(remotePath);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public List<TrackedItem> DownloadQueue()
    {
        return _trackedFiles.Values.Where(item => item.LocalFileSize == 0 && item.RemoteFileSize > 0 && item.LocalFileSize != item.RemoteFileSize).OrderBy(item => item.LastModified).ToList();
    }

}
