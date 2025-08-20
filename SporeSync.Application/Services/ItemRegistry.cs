using System;
using System.Collections;
using System.Collections.Concurrent;
using SporeSync.Domain.Models;

namespace SporeSync.Application;

public class ItemRegistry : IEnumerable<TrackedItem>
{

    private readonly ConcurrentDictionary<String, TrackedItem> _trackedFiles;

    public ItemRegistry()
    {
        _trackedFiles = new ConcurrentDictionary<string, TrackedItem>();
    }

    public void Add(TrackedItem item)
    {
        _trackedFiles.TryAdd(item.Id.ToString(), item);
    }

    public void Remove(TrackedItem item)
    {
        _trackedFiles.TryRemove(item.Id.ToString(), out _);
    }


    public IEnumerator<TrackedItem> GetEnumerator()
    {
        return _trackedFiles.Values.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public TrackedItem? Get(string id)
    {
        return _trackedFiles.TryGetValue(id, out var item) ? item : null;
    }

    public bool Contains(string id)
    {
        return _trackedFiles.ContainsKey(id);
    }
}
