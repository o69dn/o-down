using Microsoft.Extensions.Logging;
using o_down.Core.Models;
using o_down.Core.Pipeline;

namespace o_down.Update;

public sealed class UpdateCheckScheduler : IAsyncDisposable
{
    private readonly UpdateService _update;
    private readonly IAppSettingsStore _settings;
    private readonly TimeSpan _interval;
    private readonly ILogger<UpdateCheckScheduler>? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private UpdateCheckResult? _lastResult;

    public event EventHandler<UpdateCheckResult>? CheckCompleted;
    public event EventHandler<UpdateCheckResult>? UpdateAvailable;

    public UpdateCheckScheduler(
        UpdateService update,
        IAppSettingsStore settings,
        TimeSpan? interval = null,
        ILogger<UpdateCheckScheduler>? logger = null)
    {
        _update = update ?? throw new ArgumentNullException(nameof(update));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _interval = interval ?? TimeSpan.FromHours(6);
        _logger = logger;
    }

    public UpdateCheckResult? LastResult => _lastResult;
    public bool IsRunning => _loop is not null && !_loop.IsCompleted;

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunLoopAsync(_cts.Token));
    }

    public async Task<UpdateCheckResult> CheckNowAsync(CancellationToken ct = default)
    {
        var snapshot = _settings.Current;
        if (!snapshot.AutoUpdateEnabled)
        {
            var skipped = new UpdateCheckResult
            {
                CurrentVersion = _update.CurrentVersion,
                HasUpdate = false,
                Error = "Auto-update disabled",
            };
            _lastResult = skipped;
            CheckCompleted?.Invoke(this, skipped);
            return skipped;
        }
        var result = await _update.CheckAsync(snapshot.UpdateChannel, ct).ConfigureAwait(false);
        _lastResult = result;
        CheckCompleted?.Invoke(this, result);
        if (result.HasUpdate) UpdateAvailable?.Invoke(this, result);
        return result;
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await CheckNowAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Update check failed; will retry after interval");
                }
                try { await Task.Delay(_interval, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        if (_cts is null) return;
        try { _cts.Cancel(); } catch { /* best effort */ }
        var loop = _loop;
        if (loop is not null)
        {
            try { await loop.ConfigureAwait(false); } catch { /* best effort */ }
        }
        _cts.Dispose();
        _cts = null;
        _loop = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
