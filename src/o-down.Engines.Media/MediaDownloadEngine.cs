using System.Diagnostics;
using Microsoft.Extensions.Logging;
using o_down.Core.Abstractions;
using o_down.Core.Models;
using o_down.Core.Pipeline;

namespace o_down.Engines.Media;

public sealed class MediaDownloadEngine : IDownloadEngine, IAsyncDisposable
{
    private readonly IMediaExtractor _extractor;
    private readonly ILogger<MediaDownloadEngine>? _logger;
    private readonly Dictionary<string, RunningDownload> _running = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    private int _initialized;

    public event EventHandler<DownloadProgress>? ProgressChanged;
    public event EventHandler<DownloadProgress>? Completed;

    public MediaDownloadEngine(IMediaExtractor extractor, ILogger<MediaDownloadEngine>? logger = null)
    {
        _extractor = extractor;
        _logger = logger;
    }

    public string Name => "media";
    public DownloadKind Kind => DownloadKind.Media;
    public bool IsAvailable => _extractor.IsAvailable;

    public Task InitializeAsync(CancellationToken ct = default)
    {
        Interlocked.Exchange(ref _initialized, 1);
        return Task.CompletedTask;
    }

    public async Task ShutdownAsync(CancellationToken ct = default)
    {
        RunningDownload[] snapshot;
        lock (_lock) snapshot = _running.Values.ToArray();
        foreach (var r in snapshot)
        {
            try { r.Cts.Cancel(); } catch { }
        }
        await Task.WhenAll(snapshot.Select(r => r.Done)).ConfigureAwait(false);
    }

    public async Task<string> AddAsync(DownloadItem item, CancellationToken ct = default)
    {
        if (Volatile.Read(ref _initialized) == 0)
            await InitializeAsync(ct).ConfigureAwait(false);

        if (!_extractor.IsAvailable)
            throw new InvalidOperationException("media engine unavailable: " + _extractor.Name);

        var handle = Guid.NewGuid().ToString("N");
        var linkCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var running = new RunningDownload(item.Id, item, linkCts);

        var done = Task.Run(async () =>
        {
            try
            {
                await RunAsync(item, running, linkCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "media download failed for {Url}", item.SourceUrl);
                FireProgress(new DownloadProgress
                {
                    DownloadId = item.Id,
                    ReceivedBytes = running.LastReceived,
                    TotalBytes = running.LastTotal > 0 ? running.LastTotal : null,
                    SpeedBytesPerSecond = 0,
                    Connections = 0,
                    State = DownloadState.Failed,
                    ErrorMessage = ex.Message
                });
                FireCompleted(new DownloadProgress
                {
                    DownloadId = item.Id,
                    State = DownloadState.Failed,
                    ErrorMessage = ex.Message
                });
            }
            finally
            {
                lock (_lock) _running.Remove(handle);
                linkCts.Dispose();
            }
        }, linkCts.Token);

        lock (_lock) { running.Done = done; _running[handle] = running; }
        return handle;
    }

    private async Task RunAsync(DownloadItem item, RunningDownload running, CancellationToken ct)
    {
        FireProgress(new DownloadProgress
        {
            DownloadId = item.Id,
            State = DownloadState.FetchingMetadata,
            Connections = 0
        });

        MediaProbe probe;
        try
        {
            probe = await _extractor.ProbeAsync(item.SourceUrl, ct).ConfigureAwait(false);
        }
        catch
        {
            // Probe failed but we can still try the download directly (yt-dlp supports it without explicit probe).
            probe = new MediaProbe { Url = item.SourceUrl, Title = item.FilenameHint };
        }

        var format = await ResolveFormatAsync(item, probe, ct).ConfigureAwait(false);
        var outputTemplate = ResolveTemplate(item, probe, format);
        FireProgress(new DownloadProgress
        {
            DownloadId = item.Id,
            State = DownloadState.Running,
            Connections = 0
        });

        await _extractor.DownloadAsync(item, format, outputTemplate, progress: MakeProgressSink(item, running), ct).ConfigureAwait(false);

        FireProgress(new DownloadProgress
        {
            DownloadId = item.Id,
            ReceivedBytes = running.LastTotal > 0 ? running.LastTotal : running.LastReceived,
            TotalBytes = running.LastTotal > 0 ? running.LastTotal : null,
            SpeedBytesPerSecond = 0,
            State = DownloadState.Completed
        });
        FireCompleted(new DownloadProgress
        {
            DownloadId = item.Id,
            ReceivedBytes = running.LastTotal > 0 ? running.LastTotal : running.LastReceived,
            TotalBytes = running.LastTotal > 0 ? running.LastTotal : null,
            State = DownloadState.Completed
        });
    }

    private Task<MediaFormat> ResolveFormatAsync(DownloadItem item, MediaProbe probe, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(item.MediaFormatId))
        {
            var explicitFormat = probe.Formats.FirstOrDefault(f => f.Id == item.MediaFormatId);
            if (explicitFormat is not null) return Task.FromResult(explicitFormat);
            // Honor the explicit ID even if the probe didn't list it (e.g., user
            // picked a format from a previous probe, or the extractor returned
            // no formats).
            return Task.FromResult(new MediaFormat { Id = item.MediaFormatId, Extension = item.MediaAudioFormat ?? "mp4" });
        }
        var selected = FormatSelector.Select(probe.Formats, item.MediaFormatPreference, item.MediaFormatId);
        if (selected is not null) return Task.FromResult(selected);
        return Task.FromResult(new MediaFormat { Id = item.MediaAudioOnly ? "bestaudio/best" : "best", Extension = item.MediaAudioFormat ?? "mp4" });
    }

    private static string ResolveTemplate(DownloadItem item, MediaProbe probe, MediaFormat format)
    {
        if (!string.IsNullOrEmpty(item.MediaOutputTemplate)) return item.MediaOutputTemplate;
        var fallback = string.IsNullOrWhiteSpace(probe.Title) ? item.FilenameHint : probe.Title;
        var ctx = new MediaTemplateContext
        {
            Title = probe.Title,
            Id = probe.Site ?? string.Empty,
            Extension = format.Extension,
            Uploader = probe.Uploader,
            FallbackTitle = fallback
        };
        var resolved = OutputTemplateResolver.Resolve("%(title)s.%(ext)s", ctx);
        return Path.Combine(item.DestinationDirectory, resolved);
    }

    private Action<DownloadProgress>? MakeProgressSink(DownloadItem item, RunningDownload running)
    {
        return p =>
        {
            running.LastReceived = p.ReceivedBytes;
            running.LastTotal = p.TotalBytes ?? 0;
            FireProgress(new DownloadProgress
            {
                DownloadId = item.Id,
                ReceivedBytes = p.ReceivedBytes,
                TotalBytes = p.TotalBytes,
                SpeedBytesPerSecond = p.SpeedBytesPerSecond,
                State = DownloadState.Running,
                Eta = p.Eta
            });
        };
    }

    private void FireProgress(DownloadProgress p) => ProgressChanged?.Invoke(this, p);
    private void FireCompleted(DownloadProgress p) => Completed?.Invoke(this, p);

    public Task PauseAsync(string engineHandle, CancellationToken ct = default)
    {
        if (TryGet(engineHandle, out var r))
        {
            try { r.Cts.Cancel(); } catch { }
        }
        return Task.CompletedTask;
    }

    public Task ResumeAsync(string engineHandle, CancellationToken ct = default)
    {
        // yt-dlp has no native pause/resume; resuming a media download is a no-op
        // (the user must remove and re-add the item). Logged for future hook.
        _logger?.LogDebug("media engine resume is a no-op for {Handle}", engineHandle);
        return Task.CompletedTask;
    }

    public async Task RemoveAsync(string engineHandle, bool deleteFiles, CancellationToken ct = default)
    {
        if (TryGet(engineHandle, out var r))
        {
            try { r.Cts.Cancel(); } catch { }
            try { await r.Done.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false); } catch { }
            if (deleteFiles)
            {
                try
                {
                    var item = r.Item;
                    if (!string.IsNullOrEmpty(item.FinalPath) && File.Exists(item.FinalPath))
                        File.Delete(item.FinalPath);
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "delete final file failed");
                }
            }
            lock (_lock) _running.Remove(engineHandle);
        }
    }

    public async Task ForceRemoveAsync(string engineHandle, CancellationToken ct = default)
    {
        await RemoveAsync(engineHandle, deleteFiles: true, ct).ConfigureAwait(false);
    }

    public Task PurgeCompletedResultsAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task SetBandwidthLimitAsync(long? bytesPerSecond, CancellationToken ct = default)
    {
        // yt-dlp honors --limit-rate but it has to be set at launch; ignore mid-flight for now.
        _logger?.LogDebug("media engine bandwidth limit not applied to running downloads");
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DownloadProgress>> QueryAllAsync(CancellationToken ct = default)
    {
        List<DownloadProgress> snapshot;
        lock (_lock) snapshot = _running.Values.Select(r => r.Snapshot()).ToList();
        return Task.FromResult<IReadOnlyList<DownloadProgress>>(snapshot);
    }

    public Task<DownloadProgress?> QueryAsync(string engineHandle, CancellationToken ct = default)
    {
        if (TryGet(engineHandle, out var r))
            return Task.FromResult<DownloadProgress?>(r.Snapshot());
        return Task.FromResult<DownloadProgress?>(null);
    }

    private bool TryGet(string handle, out RunningDownload r)
    {
        lock (_lock) return _running.TryGetValue(handle, out r!);
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync().ConfigureAwait(false);
    }

    private sealed class RunningDownload
    {
        public Guid DownloadId { get; }
        public DownloadItem Item { get; }
        public CancellationTokenSource Cts { get; }
        public Task Done { get; set; } = Task.CompletedTask;
        public long LastReceived;
        public long LastTotal;

        public RunningDownload(Guid downloadId, DownloadItem item, CancellationTokenSource cts)
        {
            DownloadId = downloadId;
            Item = item;
            Cts = cts;
        }

        public DownloadProgress Snapshot() => new()
        {
            DownloadId = DownloadId,
            ReceivedBytes = LastReceived,
            TotalBytes = LastTotal > 0 ? LastTotal : null,
            State = DownloadState.Running,
            Connections = 0
        };
    }
}
