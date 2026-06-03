using System.Text.Json.Nodes;
using o_down.Core.Abstractions;
using o_down.Core.Models;
using Microsoft.Extensions.Logging;

namespace o_down.Engines.Aria2;

public sealed class Aria2DownloadEngine : IDownloadEngine, IAsyncDisposable
{
    private readonly Aria2HostProcess _host;
    private readonly Aria2JsonRpcClient _rpc;
    private readonly ILogger<Aria2DownloadEngine>? _logger;
    private readonly Dictionary<string, EngineHandle> _handles = new(StringComparer.Ordinal);
    private readonly object _handleLock = new();
    private readonly CancellationTokenSource _pumpCts = new();
    private Task? _pumpTask;
    private int _started;

    public Aria2DownloadEngine(Aria2HostProcess host, ILogger<Aria2DownloadEngine>? logger = null)
    {
        _host = host;
        _rpc = new Aria2JsonRpcClient(new Uri($"http://127.0.0.1:{host.Port}/jsonrpc"), host.Secret);
        _logger = logger;
    }

    public string Name => "aria2";
    public DownloadKind Kind => DownloadKind.Http;
    public bool IsAvailable => File.Exists(_host.GetType()
        .GetField("_exePath", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?
        .GetValue(_host) as string ?? string.Empty);

    public event EventHandler<DownloadProgress>? ProgressChanged;
    public event EventHandler<DownloadProgress>? Completed;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _host.StartAsync(ct).ConfigureAwait(false);
        if (Interlocked.Exchange(ref _started, 1) == 0)
            _pumpTask = Task.Run(() => ProgressPumpAsync(_pumpCts.Token));
    }

    public async Task ShutdownAsync(CancellationToken ct = default)
    {
        try
        {
            await _pumpCts.CancelAsync().ConfigureAwait(false);
            if (_pumpTask is not null)
            {
                try { await _pumpTask.ConfigureAwait(false); } catch { }
            }
        }
        catch { }

        try
        {
            await _rpc.CallAsync("aria2.shutdown", ct: ct).ConfigureAwait(false);
        }
        catch { }
        await _host.DisposeAsync().ConfigureAwait(false);
    }

    public async Task<string> AddAsync(DownloadItem item, CancellationToken ct = default)
    {
        var uris = new List<string> { item.SourceUrl };
        if (item.Mirrors is { Count: > 0 })
        {
            var extras = item.Mirrors
                .OrderBy(m => m.Priority)
                .Select(m => m.Url)
                .Where(u => !string.Equals(u, item.SourceUrl, StringComparison.OrdinalIgnoreCase));
            uris.AddRange(extras);
        }

        var options = Aria2Options.FromDownloadItem(item);
        var rpcOptions = options.ToRpcOptions();

        var gid = (string?)await _rpc.CallAsync("aria2.addUri",
            uris.ToArray(),
            rpcOptions,
            ct).ConfigureAwait(false) ?? throw new InvalidOperationException("aria2 returned no gid");

        lock (_handleLock)
        {
            _handles[gid] = new EngineHandle(item.Id, options);
        }
        return gid;
    }

    public async Task PauseAsync(string engineHandle, CancellationToken ct = default) =>
        await _rpc.CallAsync("aria2.pause", engineHandle, ct).ConfigureAwait(false);

    public async Task ResumeAsync(string engineHandle, CancellationToken ct = default) =>
        await _rpc.CallAsync("aria2.unpause", engineHandle, ct).ConfigureAwait(false);

    public async Task RemoveAsync(string engineHandle, bool deleteFiles, CancellationToken ct = default)
    {
        if (deleteFiles)
            await _rpc.CallAsync("aria2.removeDownloadResult", engineHandle, ct).ConfigureAwait(false);
        else
            await _rpc.CallAsync("aria2.remove", engineHandle, ct).ConfigureAwait(false);
        lock (_handleLock) _handles.Remove(engineHandle);
    }

    public async Task ForceRemoveAsync(string engineHandle, CancellationToken ct = default)
    {
        EngineHandle? removed;
        lock (_handleLock) { _handles.TryGetValue(engineHandle, out removed); _handles.Remove(engineHandle); }
        try
        {
            await _rpc.CallAsync("aria2.forceRemove", engineHandle, ct).ConfigureAwait(false);
        }
        finally
        {
            if (removed is not null)
            {
                var path = Path.Combine(removed.Options.Dir ?? string.Empty, removed.Options.Out ?? engineHandle);
                try { if (File.Exists(path)) File.Delete(path); } catch { }
                var aria2Ctl = path + ".aria2";
                try { if (File.Exists(aria2Ctl)) File.Delete(aria2Ctl); } catch { }
            }
        }
    }

    public async Task PurgeCompletedResultsAsync(CancellationToken ct = default)
    {
        var stopped = (JsonArray?)await _rpc.CallAsync("aria2.tellStopped",
            0, 1000,
            new[] { "gid", "status" },
            ct).ConfigureAwait(false);
        if (stopped is null) return;
        foreach (var entry in stopped)
        {
            if (entry is not JsonObject o) continue;
            var gid = o["gid"]?.GetValue<string>();
            if (gid is null) continue;
            try { await _rpc.CallAsync("aria2.removeDownloadResult", gid, ct).ConfigureAwait(false); } catch { }
        }
    }

    public async Task SetBandwidthLimitAsync(long? bytesPerSecond, CancellationToken ct = default)
    {
        if (bytesPerSecond is null)
        {
            await _rpc.CallAsync("aria2.changeGlobalOption",
                new Dictionary<string, object?> { ["max-overall-download-limit"] = "0" },
                ct).ConfigureAwait(false);
        }
        else
        {
            await _rpc.CallAsync("aria2.changeGlobalOption",
                new Dictionary<string, object?> { ["max-overall-download-limit"] = bytesPerSecond.Value.ToString() },
                ct).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyDictionary<string, object?>?> GetOptionAsync(string engineHandle, CancellationToken ct = default)
    {
        try
        {
            var node = await _rpc.GetOptionAsync(engineHandle, ct).ConfigureAwait(false);
            if (node is not JsonObject obj) return null;
            var dict = new Dictionary<string, object?>(obj.Count);
            foreach (var kv in obj)
                dict[kv.Key] = Unwrap(kv.Value);
            return dict;
        }
        catch (HttpRequestException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static object? Unwrap(JsonNode? node)
    {
        if (node is null) return null;
        if (node is JsonValue v)
        {
            try { return v.GetValue<long>(); } catch { }
            try { return v.GetValue<bool>(); } catch { }
            try { return v.GetValue<double>(); } catch { }
            string? s = null;
            try { s = v.GetValue<string>(); } catch { }
            if (s is null) return null;
            if (long.TryParse(s, out var l)) return l;
            if (bool.TryParse(s, out var b)) return b;
            if (double.TryParse(s, out var d)) return d;
            return s;
        }
        if (node is JsonArray arr) return arr.Select(Unwrap).ToArray();
        if (node is JsonObject obj) return obj.ToDictionary(kv => kv.Key, kv => Unwrap(kv.Value));
        return null;
    }

    public async Task ChangeOptionAsync(string engineHandle, IReadOnlyDictionary<string, object?> options, CancellationToken ct = default)
    {
        await _rpc.ChangeOptionAsync(engineHandle, options, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> GetStoppedListAsync(int offset = 0, int num = 1000, CancellationToken ct = default)
    {
        var node = (JsonNode?)await _rpc.CallAsync("aria2.tellStopped",
            offset, num,
            new[] { "gid" },
            ct).ConfigureAwait(false);
        if (node is not JsonArray arr) return Array.Empty<string>();
        var list = new List<string>(arr.Count);
        foreach (var entry in arr)
        {
            if (entry is JsonObject o && o["gid"]?.GetValue<string>() is { } gid)
                list.Add(gid);
        }
        return list;
    }

    public async Task<IReadOnlyList<DownloadProgress>> QueryAllAsync(CancellationToken ct = default) =>
        await ListAsync(ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<DownloadProgress>> ListAsync(CancellationToken ct = default)
    {
        var keys = new[] { "gid", "status", "totalLength", "completedLength", "downloadSpeed", "connections" };
        var active = (JsonArray?)await _rpc.CallAsync("aria2.tellActive", keys, ct).ConfigureAwait(false);
        var waiting = (JsonArray?)await _rpc.CallAsync("aria2.tellWaiting", 0, 1000, keys, ct).ConfigureAwait(false);
        var list = new List<DownloadProgress>();
        if (active is not null)
        {
            foreach (var n in active)
                if (n is JsonNode node) list.Add(MapStatusNode(node));
        }
        if (waiting is not null)
        {
            foreach (var n in waiting)
                if (n is JsonNode node) list.Add(MapStatusNode(node));
        }
        return list;
    }

    public async Task<DownloadProgress?> QueryAsync(string engineHandle, CancellationToken ct = default)
    {
        var keys = new[] { "gid", "totalLength", "completedLength", "downloadSpeed", "connections", "status", "errorMessage" };
        try
        {
            var node = (JsonNode?)await _rpc.TellStatusAsync(engineHandle, keys, ct).ConfigureAwait(false);
            return node is null ? null : MapStatusNode(node);
        }
        catch (HttpRequestException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private async Task ProgressPumpAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PumpOnceAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "aria2 progress pump error");
            }
            try { await Task.Delay(500, ct).ConfigureAwait(false); } catch { return; }
        }
    }

    private async Task PumpOnceAsync(CancellationToken ct)
    {
        List<string> gids;
        lock (_handleLock)
        {
            gids = _handles.Keys.ToList();
        }
        if (gids.Count == 0) return;

        var keys = new[] { "gid", "status", "totalLength", "completedLength", "downloadSpeed", "uploadSpeed", "connections", "errorCode", "errorMessage", "dir", "files" };
        var calls = gids.Select(g => ("aria2.tellStatus", new object?[] { g, keys })).ToList();
        IReadOnlyList<JsonNode?> batch;
        try
        {
            batch = await _rpc.MultiCallAsync(calls, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "aria2 multiCall failed, falling back to single calls");
            var fallback = new List<JsonNode?>(gids.Count);
            foreach (var g in gids)
            {
                try { fallback.Add(await _rpc.TellStatusAsync(g, keys, ct).ConfigureAwait(false)); }
                catch { fallback.Add(null); }
            }
            batch = fallback;
        }

        var finished = new List<(string gid, DownloadProgress p)>();
        for (int i = 0; i < gids.Count && i < batch.Count; i++)
        {
            var node = batch[i];
            if (node is null) continue;
            var progress = MapStatusNode(node);
            var gid = gids[i];
            ProgressChanged?.Invoke(this, progress);
            if (progress.State is DownloadState.Completed or DownloadState.Failed or DownloadState.Removed)
                finished.Add((gid, progress));
        }

        foreach (var (gid, progress) in finished)
        {
            try
            {
                await _rpc.RemoveDownloadResultAsync(gid, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "removeDownloadResult failed for {Gid}", gid);
            }
            ProgressChanged?.Invoke(this, progress);
            Completed?.Invoke(this, progress);
            lock (_handleLock) _handles.Remove(gid);
        }
    }

    private static DownloadProgress MapStatusNode(JsonNode n)
    {
        return new DownloadProgress
        {
            DownloadId = Guid.TryParse(n["gid"]?.GetValue<string>() ?? string.Empty, out var g) ? g : Guid.Empty,
            TotalBytes = AsLong(n["totalLength"], -1),
            ReceivedBytes = AsLong(n["completedLength"], 0),
            SpeedBytesPerSecond = AsLong(n["downloadSpeed"], 0),
            Connections = (int)AsLong(n["connections"], 0),
            State = MapState(n["status"]?.GetValue<string>()),
            ErrorMessage = n["errorMessage"]?.GetValue<string>()
        };
    }

    private static long AsLong(JsonNode? n, long fallback)
    {
        if (n is null) return fallback;
        if (n is JsonValue v)
        {
            try { return v.GetValue<long>(); } catch { }
            try { return v.GetValue<int>(); } catch { }
            try { return long.Parse(v.GetValue<string>()); } catch { }
        }
        return fallback;
    }

    private static DownloadState MapState(string? s) => s switch
    {
        "active" => DownloadState.Running,
        "waiting" => DownloadState.Queued,
        "paused" => DownloadState.Paused,
        "error" => DownloadState.Failed,
        "complete" => DownloadState.Completed,
        "removed" => DownloadState.Removed,
        _ => DownloadState.Queued
    };

    public async ValueTask DisposeAsync()
    {
        try { await _pumpCts.CancelAsync().ConfigureAwait(false); } catch { }
        if (_pumpTask is not null)
        {
            try { await _pumpTask.ConfigureAwait(false); } catch { }
        }
        _pumpCts.Dispose();
        await _rpc.DisposeAsync().ConfigureAwait(false);
        await _host.DisposeAsync().ConfigureAwait(false);
    }

    private sealed record EngineHandle(Guid DownloadId, Aria2Options Options);
}
