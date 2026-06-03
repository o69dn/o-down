using System.Net;
using System.Net.Sockets;
using o_down.Core.Abstractions;
using o_down.Core.Models;
using o_down.Engines.Aria2;
using Xunit;
using Xunit.Abstractions;

namespace o_down.Engines.Aria2.Tests;

[Trait("Category", "Integration")]
public class Aria2RealIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private static readonly string[] CandidatePaths = new[]
    {
        Path.Combine("tools", "aria2c", "x64", "aria2c.exe"),
        Path.Combine("..", "..", "..", "..", "tools", "aria2c", "x64", "aria2c.exe"),
        Path.Combine("..", "..", "..", "..", "..", "tools", "aria2c", "x64", "aria2c.exe"),
        @"C:\Program Files\aria2\aria2c.exe"
    };

    private readonly string _workDir = Path.Combine(Path.GetTempPath(), "odown-aria2-it-" + Guid.NewGuid().ToString("N").Substring(0, 8));
    private readonly string _downloadDir;
    private readonly string _configDir;
    private readonly string _seedFile;
    private readonly int _filePort;
    private readonly string _aria2Port;
    private string? _aria2Path;
    private HttpListener? _fileServer;
    private Aria2HostProcess? _host;
    private Aria2DownloadEngine? _engine;

    public Aria2RealIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _downloadDir = Path.Combine(_workDir, "downloads");
        _configDir = Path.Combine(_workDir, "aria2-config");
        _seedFile = Path.Combine(_workDir, "seed.bin");
        _aria2Port = TcpPortHelper.GetFreePort().ToString();
        _filePort = TcpPortHelper.GetFreePort();
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_workDir);
        Directory.CreateDirectory(_downloadDir);
        Directory.CreateDirectory(_configDir);

        _aria2Path = CandidatePaths
            .Select(Path.GetFullPath)
            .FirstOrDefault(File.Exists);

        if (_aria2Path is null)
        {
            _output.WriteLine("aria2c.exe not found in any candidate path; tests will be marked Failed.");
            _output.WriteLine("Drop a binary at one of:");
            foreach (var p in CandidatePaths) _output.WriteLine("  - " + p);
            return;
        }

        var rng = new Random(42);
        var buf = new byte[4 * 1024 * 1024];
        rng.NextBytes(buf);
        await File.WriteAllBytesAsync(_seedFile, buf);

        StartFileServer();

        _host = new Aria2HostProcess(_aria2Path, _downloadDir, _configDir, int.Parse(_aria2Port), "odown-test");
        _engine = new Aria2DownloadEngine(_host);
        await _engine.InitializeAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        if (_engine is not null) await _engine.DisposeAsync().ConfigureAwait(false);
        try { _fileServer?.Stop(); _fileServer?.Close(); } catch { }
        if (Environment.GetEnvironmentVariable("ODOWN_KEEP_TEST_DIR") is null)
        {
            try { if (Directory.Exists(_workDir)) Directory.Delete(_workDir, recursive: true); } catch { }
        }
        else
        {
            Console.WriteLine("Test artifacts preserved at: " + _workDir);
        }
    }

    private void StartFileServer()
    {
        var prefix = $"http://127.0.0.1:{_filePort}/";
        _fileServer = new HttpListener();
        _fileServer.Prefixes.Add(prefix);
        _fileServer.Start();

        _ = Task.Run(async () =>
        {
            while (_fileServer.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _fileServer.GetContextAsync().ConfigureAwait(false); }
                catch { return; }
                try
                {
                    var data = await File.ReadAllBytesAsync(_seedFile).ConfigureAwait(false);
                    ctx.Response.ContentType = "application/octet-stream";
                    ctx.Response.ContentLength64 = data.Length;
                    ctx.Response.Headers["Accept-Ranges"] = "bytes";

                    var range = ctx.Request.Headers["Range"];
                    ReadOnlyMemory<byte> slice;
                    if (!string.IsNullOrEmpty(range) && range.StartsWith("bytes="))
                    {
                        var span = range.Substring("bytes=".Length).Split('-');
                        var start = long.Parse(span[0]);
                        var end = span.Length > 1 && !string.IsNullOrEmpty(span[1]) ? long.Parse(span[1]) : data.Length - 1;
                        var len = end - start + 1;
                        ctx.Response.StatusCode = 206;
                        ctx.Response.ContentLength64 = len;
                        ctx.Response.Headers["Content-Range"] = $"bytes {start}-{end}/{data.Length}";
                        slice = data.AsMemory((int)start, (int)len);
                    }
                    else
                    {
                        slice = data;
                    }
                    const int chunkSize = 16 * 1024;
                    for (int offset = 0; offset < slice.Length; offset += chunkSize)
                    {
                        var len = Math.Min(chunkSize, slice.Length - offset);
                        await ctx.Response.OutputStream.WriteAsync(slice.Slice(offset, len)).ConfigureAwait(false);
                        await ctx.Response.OutputStream.FlushAsync().ConfigureAwait(false);
                        await Task.Delay(20).ConfigureAwait(false);
                    }
                    ctx.Response.OutputStream.Close();
                }
                catch { try { ctx.Response.Abort(); } catch { } }
            }
        });
    }

    [Fact]
    public async Task EndToEnd_DownloadsFile_ViaRealAria2()
    {
        RequireAria2();
        RequireFileServer();

        var item = new DownloadItem
        {
            SourceUrl = $"http://127.0.0.1:{_filePort}/seed.bin",
            DestinationDirectory = _downloadDir,
            FilenameHint = "downloaded.bin",
            MaxConnections = 4,
            MaxConnectionsPerServer = 4,
            MinSplitSize = 1L * 1024 * 1024
        };

        var completed = new TaskCompletionSource<DownloadProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        _engine!.Completed += (_, p) => completed.TrySetResult(p);
        _engine.ProgressChanged += (_, p) =>
        {
            if (p.State == DownloadState.Completed)
                completed.TrySetResult(p);
        };

        var gid = await _engine.AddAsync(item);
        var finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(60)));
        if (finished != completed.Task)
        {
            var p = await _engine.QueryAsync(gid);
            _output.WriteLine($"diagnostic: QueryAsync returned {p?.State} recv={p?.ReceivedBytes} total={p?.TotalBytes}");
            Assert.True(finished == completed.Task, $"download did not complete within 60s. QueryAsync returned state={p?.State}");
        }

        var progress = await completed.Task;
        Assert.Equal(DownloadState.Completed, progress.State);
        var downloaded = Path.Combine(_downloadDir, "downloaded.bin");
        Assert.True(File.Exists(downloaded), $"file not found at {downloaded}");
        Assert.Equal(new FileInfo(_seedFile).Length, new FileInfo(downloaded).Length);

        var recheck = await _engine.QueryAsync(gid);
        Assert.Null(recheck);
        await _engine.PurgeCompletedResultsAsync();
    }

    [Fact]
    public async Task ForceRemoveAsync_StopsInFlightDownload()
    {
        RequireAria2();
        RequireFileServer();

        var item = new DownloadItem
        {
            SourceUrl = $"http://127.0.0.1:{_filePort}/seed.bin",
            DestinationDirectory = _downloadDir,
            FilenameHint = "forcekill.bin",
            MaxConnections = 1,
            MaxDownloadLimit = 1024
        };
        var gid = await _engine!.AddAsync(item);
        await Task.Delay(100);
        var mid = await _engine.QueryAsync(gid);
        Assert.NotNull(mid);
        await _engine.ForceRemoveAsync(gid);
        await Task.Delay(200);
        var recheck = await _engine.QueryAsync(gid);
        Assert.NotNull(recheck);
        Assert.Equal(DownloadState.Removed, recheck!.State);
        await _engine.PurgeCompletedResultsAsync();
        var recheck2 = await _engine.QueryAsync(gid);
        Assert.Null(recheck2);
        Assert.False(File.Exists(Path.Combine(_downloadDir, "forcekill.bin")));
    }

    [Fact]
    public async Task ChangeOptionAsync_AcceptsMidFlightChanges()
    {
        RequireAria2();
        RequireFileServer();

        var item = new DownloadItem
        {
            SourceUrl = $"http://127.0.0.1:{_filePort}/seed.bin",
            DestinationDirectory = _downloadDir,
            FilenameHint = "changeopt.bin",
            MaxConnections = 2
        };
        var gid = await _engine!.AddAsync(item);
        await Task.Delay(150);

        var mid = await _engine.QueryAsync(gid);
        Assert.NotNull(mid);
        Assert.Equal(DownloadState.Running, mid!.State);

        await _engine.ChangeOptionAsync(gid, new Dictionary<string, object?>
        {
            ["user-agent"] = "o-down/1.0 (mid-flight)",
            ["header"] = new[] { "X-Test: changed" }
        });

        var stillRunning = await _engine.QueryAsync(gid);
        Assert.NotNull(stillRunning);
        Assert.Equal(DownloadState.Running, stillRunning!.State);

        await _engine.ForceRemoveAsync(gid);
    }

    [Fact]
    public async Task PurgeCompletedResultsAsync_RemovesStoppedResults()
    {
        RequireAria2();
        RequireFileServer();

        var item = new DownloadItem
        {
            SourceUrl = $"http://127.0.0.1:{_filePort}/seed.bin",
            DestinationDirectory = _downloadDir,
            FilenameHint = "purgetarget.bin",
            MaxConnections = 1,
            MaxDownloadLimit = 1024
        };
        var gid = await _engine!.AddAsync(item);
        await Task.Delay(100);
        var mid = await _engine.QueryAsync(gid);
        Assert.NotNull(mid);

        await _engine.ForceRemoveAsync(gid);
        await Task.Delay(200);

        var tellStopped = await _engine.GetStoppedListAsync();
        Assert.Contains(tellStopped, g => g == gid);

        await _engine.PurgeCompletedResultsAsync();

        var tellStoppedAfter = await _engine.GetStoppedListAsync();
        Assert.DoesNotContain(tellStoppedAfter, g => g == gid);
    }

    private void RequireAria2()
    {
        if (_aria2Path is null)
            throw new InvalidOperationException("aria2c.exe not available. Drop a binary at tools/aria2c/x64/aria2c.exe to run integration tests.");
        if (_engine is null)
            throw new InvalidOperationException("aria2 engine failed to initialize; check that the binary is a working aria2c build.");
    }

    private void RequireFileServer()
    {
        if (_fileServer is not { IsListening: true })
            throw new InvalidOperationException("test file server failed to start");
    }
}

internal static class TcpPortHelper
{
    public static int GetFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
