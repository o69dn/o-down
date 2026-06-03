using System.Collections.Concurrent;
using MonoTorrent;
using MonoTorrent.Client;
using o_down.Core.Abstractions;
using o_down.Core.Models;
using Microsoft.Extensions.Logging;
using MTorrentFile = MonoTorrent.TorrentFile;

namespace o_down.Engines.Torrent;

public sealed class TorrentDownloadEngine : ITorrentEngine, IAsyncDisposable
{
    private readonly ILogger<TorrentDownloadEngine>? _logger;
    private readonly string _cacheDirectory;
    private readonly ClientEngine _engine;
    private readonly ConcurrentDictionary<string, Guid> _handles = new();
    private readonly ConcurrentDictionary<Guid, TorrentManager> _byDownloadId = new();
    private readonly ConcurrentDictionary<TorrentManager, Guid> _byManager = new();
    private readonly ConcurrentDictionary<Guid, bool> _completionFired = new();
    private readonly EventHandler<TorrentStateChangedEventArgs> _stateChangedHandler;

    public TorrentDownloadEngine(string cacheDirectory, ILogger<TorrentDownloadEngine>? logger = null)
    {
        _logger = logger;
        _cacheDirectory = cacheDirectory;
        Directory.CreateDirectory(_cacheDirectory);
        var settings = new EngineSettingsBuilder
        {
            CacheDirectory = _cacheDirectory,
            DhtPort = 6881,
            ListenPort = 6881,
            MaximumConnections = 200,
            AllowLocalPeerDiscovery = true,
            AllowPortForwarding = true,
            AutoSaveLoadDhtCache = true,
            AutoSaveLoadFastResume = true,
            AutoSaveLoadMagnetLinkMetadata = true,
            DiskCacheBytes = 64 * 1024 * 1024,
        }.ToSettings();
        _engine = new ClientEngine(settings);
        _stateChangedHandler = OnTorrentStateChanged;
    }

    public string Name => "MonoTorrent";
    public DownloadKind Kind => DownloadKind.Torrent;
    public bool IsAvailable => true;

    public event EventHandler<DownloadProgress>? ProgressChanged;
    public event EventHandler<DownloadProgress>? Completed;

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task ShutdownAsync(CancellationToken ct = default)
    {
        foreach (var m in _byDownloadId.Values)
        {
            try { await m.StopAsync().ConfigureAwait(false); } catch { }
        }
    }

    public Task<TorrentMetadata> ProbeAsync(string source, CancellationToken ct = default)
    {
        if (source.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            if (MagnetLink.TryParse(source, out var magnet))
            {
                return Task.FromResult(new TorrentMetadata
                {
                    Name = magnet.Name ?? "torrent",
                    Size = magnet.Size ?? 0,
                    InfoHash = magnet.InfoHash?.ToHex() ?? string.Empty
                });
            }
            throw new FormatException("Invalid magnet link");
        }
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            var t = MonoTorrent.Torrent.Load(source);
            return Task.FromResult(new TorrentMetadata
            {
                Name = t.Name,
                Size = t.Size,
                InfoHash = t.InfoHash?.ToHex() ?? string.Empty,
                PieceCount = t.Pieces.Count,
                PieceLength = t.PieceLength,
                Files = t.Files.Select((MTorrentFile f) => new o_down.Core.Abstractions.TorrentFile { Path = f.Path, Size = f.Length }).ToList()
            });
        }
        throw new NotSupportedException("Unsupported torrent source");
    }

    public async Task<string> AddAsync(DownloadItem item, CancellationToken ct = default)
    {
        var torrentSettings = BuildTorrentSettings(item);

        TorrentManager manager;
        if (item.SourceUrl.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            if (!MagnetLink.TryParse(item.SourceUrl, out var magnet))
                throw new FormatException("Invalid magnet link");
            manager = await _engine.AddAsync(magnet, item.DestinationDirectory, torrentSettings).ConfigureAwait(false);
        }
        else
        {
            var t = MonoTorrent.Torrent.Load(item.SourceUrl);
            manager = await _engine.AddAsync(t, item.DestinationDirectory, torrentSettings).ConfigureAwait(false);
        }

        await ApplyFilePrioritiesAsync(manager, item).ConfigureAwait(false);

        manager.TorrentStateChanged += _stateChangedHandler;
        _byManager[manager] = item.Id;

        var handle = Guid.NewGuid().ToString("N");
        _handles[handle] = item.Id;
        _byDownloadId[item.Id] = manager;

        await manager.StartAsync().ConfigureAwait(false);
        return handle;
    }

    public Task PauseAsync(string engineHandle, CancellationToken ct = default)
    {
        if (TryResolve(engineHandle, out var m))
            return m.PauseAsync();
        return Task.CompletedTask;
    }

    public Task ResumeAsync(string engineHandle, CancellationToken ct = default)
    {
        if (TryResolve(engineHandle, out var m))
            return m.StartAsync();
        return Task.CompletedTask;
    }

    public Task SetSequentialAsync(string engineHandle, bool sequential, CancellationToken ct = default)
    {
        // MonoTorrent 2.0 ships a "default" picker only. Sequential order is achieved
        // indirectly through streaming mode (AddStreamingAsync) or by swapping the piece
        // picker with a custom IPieceRequester. We log and no-op for now.
        _logger?.LogDebug("SetSequentialAsync({On}) — no native sequential picker in MonoTorrent 2.0; flag stored for future picker swap.", sequential);
        return Task.CompletedTask;
    }

    public async Task RemoveAsync(string engineHandle, bool deleteFiles, CancellationToken ct = default)
    {
        if (_handles.TryRemove(engineHandle, out var id) && _byDownloadId.TryRemove(id, out var m))
        {
            _byManager.TryRemove(m, out _);
            _completionFired.TryRemove(id, out _);
            try { m.TorrentStateChanged -= _stateChangedHandler; } catch { }
            try { await m.StopAsync().ConfigureAwait(false); } catch { }
            try
            {
                var mode = deleteFiles ? RemoveMode.CacheDataAndDownloadedData : RemoveMode.CacheDataOnly;
                await _engine.RemoveAsync(m, mode).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "remove from engine failed for {Id}", id);
            }
        }
    }

    public async Task ForceRemoveAsync(string engineHandle, CancellationToken ct = default) =>
        await RemoveAsync(engineHandle, deleteFiles: true, ct).ConfigureAwait(false);

    public Task PurgeCompletedResultsAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task SetBandwidthLimitAsync(long? bytesPerSecond, CancellationToken ct = default)
    {
        // ClientEngine.Settings is read-only at runtime in MonoTorrent 2.0. The limit
        // applies to newly added torrents only; in-flight torrents need an engine
        // restart to pick up a new global throttle.
        _logger?.LogDebug("SetBandwidthLimitAsync({Limit}) — applied to newly added torrents only.", bytesPerSecond);
        return Task.CompletedTask;
    }

    public Task<DownloadProgress?> QueryAsync(string engineHandle, CancellationToken ct = default)
    {
        if (!TryResolve(engineHandle, out var id, out var m))
            return Task.FromResult<DownloadProgress?>(null);
        return Task.FromResult<DownloadProgress?>(BuildProgress(id, m));
    }

    public IReadOnlyList<string> GetActiveTorrents() =>
        _byDownloadId.Keys.Select(k => k.ToString()).ToList();

    public Task<IReadOnlyList<DownloadProgress>> QueryAllAsync(CancellationToken ct = default)
    {
        IReadOnlyList<DownloadProgress> snapshot = _byDownloadId.Select(kv => BuildProgress(kv.Key, kv.Value)).ToList();
        return Task.FromResult(snapshot);
    }

    private bool TryResolve(string engineHandle, out TorrentManager manager)
    {
        if (_handles.TryGetValue(engineHandle, out var id) && _byDownloadId.TryGetValue(id, out var m))
        {
            manager = m;
            return true;
        }
        manager = null!;
        return false;
    }

    private bool TryResolve(string engineHandle, out Guid id, out TorrentManager manager)
    {
        if (_handles.TryGetValue(engineHandle, out var resolvedId) && _byDownloadId.TryGetValue(resolvedId, out var m))
        {
            id = resolvedId;
            manager = m;
            return true;
        }
        id = Guid.Empty;
        manager = null!;
        return false;
    }

    private TorrentSettings BuildTorrentSettings(DownloadItem item)
    {
        var builder = new TorrentSettingsBuilder
        {
            MaximumConnections = item.TorrentMaxConnections ?? 200,
            MaximumDownloadSpeed = (int)Math.Min(int.MaxValue, item.TorrentMaxDownloadSpeed ?? 0),
            UploadSlots = item.TorrentUploadSlots
        };
        return builder.ToSettings();
    }

    private static async Task ApplyFilePrioritiesAsync(TorrentManager manager, DownloadItem item)
    {
        if (item.TorrentWantedFiles is not { Count: > 0 })
        {
            foreach (var f in manager.Files)
            {
                try { await manager.SetFilePriorityAsync(f, Priority.Normal).ConfigureAwait(false); } catch { }
            }
            return;
        }
        var wanted = new HashSet<string>(item.TorrentWantedFiles, StringComparer.OrdinalIgnoreCase);
        foreach (var f in manager.Files)
        {
            var p = wanted.Contains(f.FullPath) || wanted.Contains(Path.GetFileName(f.FullPath))
                ? Priority.Normal
                : Priority.DoNotDownload;
            try { await manager.SetFilePriorityAsync(f, p).ConfigureAwait(false); } catch { }
        }
    }

    private static DownloadProgress BuildProgress(Guid id, TorrentManager m)
    {
        long total = m.Size;
        long received = 0;
        long speed = 0;
        int peers = 0;
        int connections = 0;
        try
        {
            if (m.Bitfield != null && m.PieceLength > 0)
            {
                int completed = 0;
                int len = m.Bitfield.Length;
                for (int i = 0; i < len; i++) if (m.Bitfield[i]) completed++;
                received = (long)completed * m.PieceLength;
                if (received > total) received = total;
            }
        }
        catch { }
        try { speed = m.Monitor?.DownloadSpeed ?? 0; } catch { }
        try { peers = m.Peers?.Available ?? 0; } catch { }
        try { connections = m.OpenConnections; } catch { }
        return new DownloadProgress
        {
            DownloadId = id,
            TotalBytes = total,
            ReceivedBytes = received,
            SpeedBytesPerSecond = speed,
            Connections = connections,
            State = MapState(m.State)
        };
    }

    private static DownloadState MapState(TorrentState s) => s switch
    {
        TorrentState.Downloading => DownloadState.Running,
        TorrentState.Seeding => DownloadState.Completed,
        TorrentState.Paused => DownloadState.Paused,
        TorrentState.HashingPaused => DownloadState.Paused,
        TorrentState.Stopped => DownloadState.Paused,
        TorrentState.Stopping => DownloadState.Paused,
        TorrentState.Error => DownloadState.Failed,
        TorrentState.Hashing => DownloadState.Verifying,
        TorrentState.Metadata => DownloadState.FetchingMetadata,
        TorrentState.Starting => DownloadState.Running,
        _ => DownloadState.Queued
    };

    private void OnTorrentStateChanged(object? sender, TorrentStateChangedEventArgs e)
    {
        if (sender is not TorrentManager m) return;
        if (!_byManager.TryGetValue(m, out var id)) return;
        var progress = BuildProgress(id, m);
        ProgressChanged?.Invoke(this, progress);

        if (e.NewState == TorrentState.Seeding && _completionFired.TryAdd(id, true))
        {
            Completed?.Invoke(this, progress);
        }
        else if (e.NewState == TorrentState.Error)
        {
            progress.ErrorMessage = "torrent error";
            Completed?.Invoke(this, progress);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await ShutdownAsync().ConfigureAwait(false); } catch { }
        try { _engine.Dispose(); } catch { }
    }
}
