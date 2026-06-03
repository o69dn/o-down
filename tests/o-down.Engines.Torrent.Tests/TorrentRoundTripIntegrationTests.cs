using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using MonoTorrent;
using MonoTorrent.BEncoding;
using MonoTorrent.Client;
using o_down.Core.Models;
using o_down.Engines.Torrent;
using Xunit;
using Xunit.Abstractions;
using MTorrent = MonoTorrent.Torrent;

namespace o_down.Engines.Torrent.Tests;

[Trait("Category", "Integration")]
public class TorrentRoundTripIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _workDir;
    private readonly string _seederCache;
    private readonly string _leecherCache;
    private readonly string _seederData;
    private readonly string _leecherData;

    public TorrentRoundTripIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _workDir = Path.Combine(Path.GetTempPath(), "odown-m5-roundtrip-" + Guid.NewGuid().ToString("N"));
        _seederCache = Path.Combine(_workDir, "seeder-cache");
        _leecherCache = Path.Combine(_workDir, "leecher-cache");
        _seederData = Path.Combine(_workDir, "seeder-data");
        _leecherData = Path.Combine(_workDir, "leecher-data");
        Directory.CreateDirectory(_seederCache);
        Directory.CreateDirectory(_leecherCache);
        Directory.CreateDirectory(_seederData);
        Directory.CreateDirectory(_leecherData);

        if (Environment.GetEnvironmentVariable("ODOWN_KEEP_TEST_DIR") != "1")
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { if (Directory.Exists(_workDir)) Directory.Delete(_workDir, true); } catch { }
            };
        }
    }

    public void Dispose()
    {
        if (Environment.GetEnvironmentVariable("ODOWN_KEEP_TEST_DIR") == "1")
            _output.WriteLine($"Test directory preserved: {_workDir}");
    }

    [Fact]
    public async Task Engine_CanDownloadFromInProcessSeeder()
    {
        var seederPort = FindFreeTcpPort();
        _output.WriteLine($"Seeder port: {seederPort}");

        var payload = new byte[32 * 1024];
        RandomNumberGenerator.Fill(payload);
        var originalHash = SHA256.HashData(payload);

        var torrentBytes = TorrentTestBuilder.BuildSingleFile("payload.bin", payload, pieceLength: 16 * 1024);
        var torrentPath = Path.Combine(_workDir, "payload.torrent");
        await File.WriteAllBytesAsync(torrentPath, torrentBytes);

        var sourceFile = Path.Combine(_seederData, "payload.bin");
        await File.WriteAllBytesAsync(sourceFile, payload);

        using var seederEngine = new ClientEngine(new EngineSettingsBuilder
        {
            CacheDirectory = _seederCache,
            DhtPort = 0,
            ListenPort = seederPort,
            AllowLocalPeerDiscovery = true,
            AutoSaveLoadDhtCache = false,
            AutoSaveLoadFastResume = false,
            AutoSaveLoadMagnetLinkMetadata = true,
        }.ToSettings());

        var torrent = await MTorrent.LoadAsync(torrentPath);
        var seederManager = await seederEngine.AddAsync(torrent, _seederData);
        await seederManager.StartAsync();
        seederManager.TorrentStateChanged += (_, e) => _output.WriteLine($"Seeder state: {e.OldState} -> {e.NewState}");
        seederManager.PeerConnected += (_, e) => _output.WriteLine($"Seeder peer connected: {e.Peer}");

        Assert.True(await TryConnectToListenerAsync(seederPort, TimeSpan.FromSeconds(10)), "Seeder never started listening");
        _output.WriteLine($"Seeder confirmed listening on {seederPort}");

        await using var leecher = new TorrentDownloadEngine(_leecherCache);
        var item = new DownloadItem
        {
            SourceUrl = torrentPath,
            DestinationDirectory = _leecherData,
        };
        var handle = await leecher.AddAsync(item);

        var leecherEngine = GetInternalEngine(leecher);
        var leecherManager = leecherEngine.Torrents.First(m => m.InfoHash == torrent.InfoHash);
        leecherManager.TorrentStateChanged += (_, e) => _output.WriteLine($"Leecher state: {e.OldState} -> {e.NewState}");
        leecherManager.PeerConnected += (_, e) => _output.WriteLine($"Leecher peer connected: {e.Peer}");

        var peerIdBytes = new byte[20];
        RandomNumberGenerator.Fill(peerIdBytes);
        var peer = new MonoTorrent.Client.Peer(new BEncodedString(peerIdBytes), new Uri($"tcp://127.0.0.1:{seederPort}"));
        var added = await leecherManager.AddPeersAsync(new[] { peer });
        _output.WriteLine($"Injected peer at tcp://127.0.0.1:{seederPort}, AddPeersAsync returned {added}");

        var completion = new TaskCompletionSource<Core.Abstractions.DownloadProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        leecher.Completed += (_, p) => completion.TrySetResult(p);

        var winner = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(45)));
        Assert.True(ReferenceEquals(winner, completion.Task), "Download did not complete within timeout");
        var final = await completion.Task;
        Assert.Equal(DownloadState.Completed, final.State);
        Assert.Equal(torrent.Size, final.TotalBytes);

        var downloadedFile = Path.Combine(_leecherData, "payload.bin");
        Assert.True(File.Exists(downloadedFile), $"Expected file at {downloadedFile}");

        var downloaded = await ReadSharedFileAsync(downloadedFile);
        Assert.Equal(originalHash, SHA256.HashData(downloaded));

        await leecher.RemoveAsync(handle, deleteFiles: true);
    }

    [Fact]
    public async Task Engine_RespectsWantedFiles_ExcludesExcluded()
    {
        var seederPort = FindFreeTcpPort();

        var bigPayload = new byte[16 * 1024];
        var smallPayload = new byte[4 * 1024];
        RandomNumberGenerator.Fill(bigPayload);
        RandomNumberGenerator.Fill(smallPayload);

        var rootDir = Path.Combine(_seederData, "root");
        Directory.CreateDirectory(rootDir);
        var bigFile = Path.Combine(rootDir, "wanted-big.bin");
        var smallFile = Path.Combine(rootDir, "not-wanted-small.bin");
        await File.WriteAllBytesAsync(bigFile, bigPayload);
        await File.WriteAllBytesAsync(smallFile, smallPayload);

        var torrentBytes = TorrentTestBuilder.BuildMultiFile("root", new[]
        {
            ("wanted-big.bin", bigPayload),
            ("not-wanted-small.bin", smallPayload),
        });
        var torrentPath = Path.Combine(_workDir, "multi.torrent");
        await File.WriteAllBytesAsync(torrentPath, torrentBytes);

        using var seederEngine = new ClientEngine(new EngineSettingsBuilder
        {
            CacheDirectory = _seederCache,
            DhtPort = 0,
            ListenPort = seederPort,
            AllowLocalPeerDiscovery = true,
            AutoSaveLoadDhtCache = false,
            AutoSaveLoadFastResume = false,
            AutoSaveLoadMagnetLinkMetadata = true,
        }.ToSettings());
        var torrent = await MTorrent.LoadAsync(torrentPath);
        var seederManager = await seederEngine.AddAsync(torrent, _seederData);
        await seederManager.StartAsync();
        seederManager.TorrentStateChanged += (_, e) => _output.WriteLine($"Seeder state: {e.OldState} -> {e.NewState}");

        await using var leecher = new TorrentDownloadEngine(_leecherCache);
        var item = new DownloadItem
        {
            SourceUrl = torrentPath,
            DestinationDirectory = _leecherData,
            TorrentWantedFiles = new List<string> { "wanted-big.bin" },
        };
        var handle = await leecher.AddAsync(item);

        var leecherEngine = GetInternalEngine(leecher);
        var leecherManager = leecherEngine.Torrents.First(m => m.InfoHash == torrent.InfoHash);
        leecherManager.TorrentStateChanged += (_, e) => _output.WriteLine($"Leecher state: {e.OldState} -> {e.NewState}");

        Assert.True(await TryConnectToListenerAsync(seederPort, TimeSpan.FromSeconds(10)), "Seeder never started listening");
        var peer = new MonoTorrent.Client.Peer(new BEncodedString(new byte[20]), new Uri($"tcp://127.0.0.1:{seederPort}"));
        var added = await leecherManager.AddPeersAsync(new[] { peer });
        _output.WriteLine($"Injected peer, AddPeersAsync returned {added}");

        var completion = new TaskCompletionSource<Core.Abstractions.DownloadProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        leecher.Completed += (_, p) => completion.TrySetResult(p);

        var winner = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(45)));
        Assert.True(ReferenceEquals(winner, completion.Task), "Download did not complete within timeout");
        await completion.Task;

        var wantedOnDisk = Path.Combine(_leecherData, "root", "wanted-big.bin");
        var notWantedOnDisk = Path.Combine(_leecherData, "root", "not-wanted-small.bin");
        Assert.True(File.Exists(wantedOnDisk));
        Assert.False(File.Exists(notWantedOnDisk));

        await leecher.RemoveAsync(handle, deleteFiles: true);
    }

    [Fact]
    public void TorrentTestBuilder_ProducesValidSingleFileTorrent()
    {
        var data = new byte[1024];
        RandomNumberGenerator.Fill(data);
        var bytes = TorrentTestBuilder.BuildSingleFile("hello.bin", data, pieceLength: 256);
        var loaded = MTorrent.Load(bytes);
        Assert.Equal("hello.bin", loaded.Name);
        Assert.Equal(data.Length, loaded.Size);
        Assert.Equal(256, loaded.PieceLength);
        Assert.Single(loaded.Files);
        Assert.Equal("hello.bin", loaded.Files[0].Path);
        Assert.Equal(data.Length, loaded.Files[0].Length);
    }

    [Fact]
    public void TorrentTestBuilder_ProducesValidMultiFileTorrent()
    {
        var a = new byte[100];
        var b = new byte[200];
        RandomNumberGenerator.Fill(a);
        RandomNumberGenerator.Fill(b);
        var bytes = TorrentTestBuilder.BuildMultiFile("root", new[] { ("a.bin", a), ("b.bin", b) }, pieceLength: 128);
        var loaded = MTorrent.Load(bytes);
        Assert.Equal(2, loaded.Files.Count);
        Assert.Equal(a.Length, loaded.Files[0].Length);
        Assert.Equal(b.Length, loaded.Files[1].Length);
    }

    private static ClientEngine GetInternalEngine(TorrentDownloadEngine engine)
    {
        var field = typeof(TorrentDownloadEngine).GetField("_engine", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (ClientEngine)field!.GetValue(engine)!;
    }

    private static int FindFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<bool> TryConnectToListenerAsync(int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromSeconds(1));
                return true;
            }
            catch
            {
                await Task.Delay(200);
            }
        }
        return false;
    }

    private static async Task<byte[]> ReadSharedFileAsync(string path)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var bytes = new byte[fs.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = await fs.ReadAsync(bytes.AsMemory(read));
                    if (n == 0) break;
                    read += n;
                }
                if (read == bytes.Length) return bytes;
            }
            catch (IOException)
            {
                await Task.Delay(100);
            }
        }
        return await File.ReadAllBytesAsync(path);
    }
}
