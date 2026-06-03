using System.Collections.Concurrent;
using o_down.Core.Abstractions;
using o_down.Core.Models;
using Microsoft.Extensions.Logging;

namespace o_down.Core.Pipeline;

public sealed class InMemoryDownloadQueue : IDownloadQueue
{
    private readonly ConcurrentDictionary<Guid, DownloadItem> _items = new();
    private readonly ILogger<InMemoryDownloadQueue>? _logger;

    public InMemoryDownloadQueue(ILogger<InMemoryDownloadQueue>? logger = null)
    {
        _logger = logger;
    }

    public event EventHandler<DownloadItem>? Enqueued;
    public event EventHandler<DownloadItem>? Dequeued;
    public event EventHandler<DownloadItem>? Reordered;

    public void Enqueue(DownloadItem item)
    {
        if (!_items.TryAdd(item.Id, item))
        {
            _items[item.Id] = item;
        }
        Enqueued?.Invoke(this, item);
    }

    public bool Remove(Guid id)
    {
        if (_items.TryRemove(id, out var item))
        {
            Dequeued?.Invoke(this, item);
            return true;
        }
        return false;
    }

    public bool Reorder(Guid id, int newPriority)
    {
        if (_items.TryGetValue(id, out var item))
        {
            item.Priority = newPriority;
            Reordered?.Invoke(this, item);
            return true;
        }
        return false;
    }

    public DownloadItem? PeekNext()
    {
        var now = DateTimeOffset.UtcNow;
        return _items.Values
            .Where(i => i.State == DownloadState.Queued
                && (!i.ScheduledAt.HasValue || i.ScheduledAt.Value <= now))
            .OrderByDescending(i => i.Priority)
            .ThenBy(i => i.CreatedAt)
            .FirstOrDefault();
    }

    public IReadOnlyList<DownloadItem> Snapshot() =>
        _items.Values.OrderByDescending(i => i.Priority).ThenBy(i => i.CreatedAt).ToList();

    public IReadOnlyList<DownloadItem> ActiveItems() =>
        _items.Values.Where(i => i.IsActive).ToList();
}
