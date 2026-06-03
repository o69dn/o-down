using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using o_down.Core.Abstractions;
using o_down.Core.Models;
using o_down.Data;

namespace o_down.App.Services;

public sealed class DownloadOrchestrator
{
    private readonly OdownDbContext _db;
    private readonly IDownloadQueue _queue;
    private readonly IDownloadRouter _router;
    private readonly ILogger<DownloadOrchestrator> _logger;
    private readonly Dictionary<Guid, IDownloadEngine> _engines = new();
    private readonly Dictionary<Guid, string> _handles = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cts;
    private readonly HashSet<int> _subscribed = new();

    public event EventHandler<DownloadProgress>? ItemProgress;
    public event EventHandler<DownloadItem>? ItemUpdated;
    public event EventHandler<DownloadItem>? ItemCompleted;

    public DownloadOrchestrator(OdownDbContext db, IDownloadQueue queue, IDownloadRouter router, ILogger<DownloadOrchestrator> logger)
    {
        _db = db;
        _queue = queue;
        _router = router;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PumpAsync(ct).ConfigureAwait(false);
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Orchestrator loop error");
            }
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var active = _queue.ActiveItems().ToList();
            int maxConcurrent = 5; // TODO: from settings
            while (active.Count < maxConcurrent)
            {
                var next = _queue.PeekNext();
                if (next is null) break;
                if (next.ScheduledAt is { } s && s > DateTimeOffset.UtcNow) break;
                var engine = await _router.PickEngineAsync(next, ct).ConfigureAwait(false);
                try
                {
                    EnsureEngineSubscribed(engine);
                    var handle = await engine.AddAsync(next, ct).ConfigureAwait(false);
                    _engines[next.Id] = engine;
                    _handles[next.Id] = handle;
                    next.State = DownloadState.Running;
                    next.StartedAt = DateTimeOffset.UtcNow;
                    _db.Downloads.Update(next);
                    await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                    ItemUpdated?.Invoke(this, next);
                    active.Add(next);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to start {Id}", next.Id);
                    next.RetryCount++;
                    if (next.RetryCount >= next.MaxRetries)
                    {
                        next.State = DownloadState.Failed;
                        next.ErrorMessage = ex.Message;
                        _db.Downloads.Update(next);
                        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                        ItemUpdated?.Invoke(this, next);
                    }
                }
            }

            try
            {
                foreach (var engine in _engines.Values.Distinct())
                {
                    await engine.PurgeCompletedResultsAsync(ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Purge completed results failed");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureEngineSubscribed(IDownloadEngine engine)
    {
        if (!_subscribed.Add(engine.GetHashCode())) return;
        engine.ProgressChanged += OnEngineProgress;
        engine.Completed += OnEngineCompleted;
    }

    private void OnEngineProgress(object? sender, DownloadProgress p)
    {
        try
        {
            if (!_handles.ContainsValue($"") && !_engines.ContainsKey(p.DownloadId))
            {
                // nothing — engine may not have an active handle for this gid
            }
            ItemProgress?.Invoke(this, p);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "OnEngineProgress error");
        }
    }

    private void OnEngineCompleted(object? sender, DownloadProgress p)
    {
        try
        {
            if (_engines.TryGetValue(p.DownloadId, out var engine) &&
                _handles.TryGetValue(p.DownloadId, out var handle))
            {
                var item = _db.Downloads.FirstOrDefault(d => d.Id == p.DownloadId);
                if (item is null) return;
                item.ReceivedBytes = p.ReceivedBytes;
                item.TotalBytes = p.TotalBytes > 0 ? p.TotalBytes.Value : item.TotalBytes;
                item.State = p.State;
                item.ErrorMessage = p.ErrorMessage;
                if (p.State == DownloadState.Completed)
                    item.CompletedAt = DateTimeOffset.UtcNow;
                _db.Downloads.Update(item);
                _db.SaveChanges();
                _engines.Remove(p.DownloadId);
                _handles.Remove(p.DownloadId);
                ItemCompleted?.Invoke(this, item);
                ItemUpdated?.Invoke(this, item);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OnEngineCompleted error for {Id}", p.DownloadId);
        }
    }

    public async Task PauseAsync(Guid id)
    {
        if (_engines.TryGetValue(id, out var engine) && _handles.TryGetValue(id, out var handle))
        {
            await engine.PauseAsync(handle).ConfigureAwait(false);
            var item = await _db.Downloads.FindAsync(id);
            if (item is not null)
            {
                item.State = DownloadState.Paused;
                _db.Downloads.Update(item);
                await _db.SaveChangesAsync();
                ItemUpdated?.Invoke(this, item);
            }
        }
    }

    public async Task ResumeAsync(Guid id)
    {
        if (_engines.TryGetValue(id, out var engine) && _handles.TryGetValue(id, out var handle))
        {
            await engine.ResumeAsync(handle).ConfigureAwait(false);
            var item = await _db.Downloads.FindAsync(id);
            if (item is not null)
            {
                item.State = DownloadState.Running;
                _db.Downloads.Update(item);
                await _db.SaveChangesAsync();
                ItemUpdated?.Invoke(this, item);
            }
        }
    }

    public async Task RemoveAsync(Guid id, bool deleteFiles)
    {
        if (_engines.TryGetValue(id, out var engine) && _handles.TryGetValue(id, out var handle))
        {
            await engine.RemoveAsync(handle, deleteFiles).ConfigureAwait(false);
            _engines.Remove(id);
            _handles.Remove(id);
        }
        else
        {
            var item = await _db.Downloads.FindAsync(id);
            if (item is not null)
            {
                item.State = DownloadState.Removed;
                _db.Downloads.Update(item);
                await _db.SaveChangesAsync();
                ItemUpdated?.Invoke(this, item);
            }
        }
    }
}
