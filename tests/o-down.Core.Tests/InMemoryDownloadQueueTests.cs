using o_down.Core.Abstractions;
using o_down.Core.Models;
using o_down.Core.Pipeline;
using Xunit;

namespace o_down.Core.Tests;

public class InMemoryDownloadQueueTests
{
    [Fact]
    public void Enqueue_ThenPeekNext_ReturnsHighestPriority()
    {
        var q = new InMemoryDownloadQueue();
        var low = new DownloadItem { Id = Guid.NewGuid(), Priority = 1, State = DownloadState.Queued };
        var high = new DownloadItem { Id = Guid.NewGuid(), Priority = 9, State = DownloadState.Queued };
        q.Enqueue(low);
        q.Enqueue(high);
        Assert.Same(high, q.PeekNext());
    }

    [Fact]
    public void PeekNext_RespectsSchedule()
    {
        var q = new InMemoryDownloadQueue();
        var future = new DownloadItem
        {
            Id = Guid.NewGuid(),
            Priority = 10,
            State = DownloadState.Queued,
            ScheduledAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        var now = new DownloadItem { Id = Guid.NewGuid(), Priority = 1, State = DownloadState.Queued };
        q.Enqueue(future);
        q.Enqueue(now);
        Assert.Same(now, q.PeekNext());
    }

    [Fact]
    public void Reorder_ChangesPriority()
    {
        var q = new InMemoryDownloadQueue();
        var item = new DownloadItem { Id = Guid.NewGuid(), Priority = 1, State = DownloadState.Queued };
        q.Enqueue(item);
        Assert.True(q.Reorder(item.Id, 99));
        Assert.Equal(99, item.Priority);
    }

    [Fact]
    public void Remove_FiresDequeued()
    {
        var q = new InMemoryDownloadQueue();
        var item = new DownloadItem { Id = Guid.NewGuid(), State = DownloadState.Queued };
        q.Enqueue(item);
        DownloadItem? removed = null;
        q.Dequeued += (_, i) => removed = i;
        q.Remove(item.Id);
        Assert.Same(item, removed);
    }
}
