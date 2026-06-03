using Cronos;
using o_down.Core.Abstractions;
using o_down.Core.Models;
using Microsoft.Extensions.Logging;

namespace o_down.Core.Pipeline;

public sealed class CronScheduler : IScheduler, IAsyncDisposable
{
    private readonly Dictionary<Guid, Schedule> _schedules = new();
    private readonly ILogger<CronScheduler>? _logger;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public CronScheduler(ILogger<CronScheduler>? logger = null)
    {
        _logger = logger;
    }

    public event EventHandler<ScheduleTick>? Tick;

    public void Register(Schedule schedule)
    {
        _schedules[schedule.Id] = schedule;
        if (_loop is null)
        {
            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => RunAsync(_cts.Token));
        }
    }

    public void Unregister(Guid id)
    {
        _schedules.Remove(id);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var last = DateTimeOffset.UtcNow;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
                var now = DateTimeOffset.UtcNow;
                foreach (var schedule in _schedules.Values.Where(s => s.IsEnabled))
                {
                    try
                    {
                        var cron = CronExpression.Parse(schedule.CronExpression);
                        var nextFromLast = cron.GetNextOccurrence(last, TimeZoneInfo.Utc);
                        if (nextFromLast.HasValue && nextFromLast.Value <= now)
                        {
                            var tick = new ScheduleTick
                            {
                                At = now,
                                ShouldStart = schedule.StartDownloads,
                                ShouldPause = schedule.PauseDownloads,
                                BandwidthLimitBytesPerSecond = schedule.BandwidthLimitBytesPerSecond
                            };
                            Tick?.Invoke(this, tick);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Schedule {Id} failed to evaluate", schedule.Id);
                    }
                }
                last = now;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Scheduler loop error");
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        return ValueTask.CompletedTask;
    }
}
