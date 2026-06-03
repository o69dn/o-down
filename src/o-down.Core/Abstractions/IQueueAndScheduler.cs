using o_down.Core.Models;

namespace o_down.Core.Abstractions;

public interface IDownloadRouter
{
    DownloadKind Classify(string url);
    Task<IDownloadEngine> PickEngineAsync(DownloadItem item, CancellationToken ct = default);
}

public interface IDownloadQueue
{
    event EventHandler<DownloadItem>? Enqueued;
    event EventHandler<DownloadItem>? Dequeued;
    event EventHandler<DownloadItem>? Reordered;
    void Enqueue(DownloadItem item);
    bool Remove(Guid id);
    bool Reorder(Guid id, int newPriority);
    DownloadItem? PeekNext();
    IReadOnlyList<DownloadItem> Snapshot();
    IReadOnlyList<DownloadItem> ActiveItems();
}

public interface IScheduler
{
    void Register(Schedule schedule);
    void Unregister(Guid id);
    event EventHandler<ScheduleTick>? Tick;
}

public sealed class ScheduleTick
{
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public bool ShouldStart { get; set; }
    public bool ShouldPause { get; set; }
    public long? BandwidthLimitBytesPerSecond { get; set; }
}
