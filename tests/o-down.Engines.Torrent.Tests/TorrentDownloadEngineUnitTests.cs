using o_down.Core.Models;
using o_down.Engines.Torrent;
using Xunit;

namespace o_down.Engines.Torrent.Tests;

public class TorrentDownloadEngineUnitTests : IAsyncDisposable
{
    private readonly string _cacheDir;
    private readonly TorrentDownloadEngine _engine;

    public TorrentDownloadEngineUnitTests()
    {
        _cacheDir = Path.Combine(Path.GetTempPath(), "odown-torrent-tests-" + Guid.NewGuid().ToString("N"));
        _engine = new TorrentDownloadEngine(_cacheDir);
    }

    public async ValueTask DisposeAsync()
    {
        await _engine.DisposeAsync();
        if (Directory.Exists(_cacheDir)) try { Directory.Delete(_cacheDir, true); } catch { }
    }

    [Fact]
    public void Constructor_CreatesCacheDirectory() =>
        Assert.True(Directory.Exists(_cacheDir));

    [Fact]
    public void Name_IsMonoTorrent() =>
        Assert.Equal("MonoTorrent", _engine.Name);

    [Fact]
    public void Kind_IsTorrent() =>
        Assert.Equal(DownloadKind.Torrent, _engine.Kind);

    [Fact]
    public void IsAvailable_IsTrue() =>
        Assert.True(_engine.IsAvailable);

    [Fact]
    public void GetActiveTorrents_InitiallyEmpty() =>
        Assert.Empty(_engine.GetActiveTorrents());

    [Fact]
    public async Task QueryAsync_UnknownHandle_ReturnsNull() =>
        Assert.Null(await _engine.QueryAsync("nonexistent"));

    [Fact]
    public async Task PauseAsync_UnknownHandle_DoesNotThrow()
    {
        await _engine.PauseAsync("nonexistent");
        await _engine.ResumeAsync("nonexistent");
    }

    [Fact]
    public async Task RemoveAsync_UnknownHandle_DoesNotThrow()
    {
        await _engine.RemoveAsync("nonexistent", deleteFiles: false);
        await _engine.ForceRemoveAsync("nonexistent");
    }

    [Fact]
    public async Task SetBandwidthLimitAsync_DoesNotThrow()
    {
        await _engine.SetBandwidthLimitAsync(1_000_000);
        await _engine.SetBandwidthLimitAsync(null);
    }

    [Fact]
    public async Task SetSequentialAsync_UnknownHandle_DoesNotThrow() =>
        await _engine.SetSequentialAsync("nonexistent", true);

    [Fact]
    public async Task PurgeCompletedResultsAsync_DoesNotThrow() =>
        await _engine.PurgeCompletedResultsAsync();

    [Fact]
    public void ProgressChanged_CanBeSubscribed()
    {
        EventHandler<Core.Abstractions.DownloadProgress>? handler = (_, _) => { };
        _engine.ProgressChanged += handler;
        _engine.ProgressChanged -= handler;
    }

    [Fact]
    public void Completed_CanBeSubscribed()
    {
        EventHandler<Core.Abstractions.DownloadProgress>? handler = (_, _) => { };
        _engine.Completed += handler;
        _engine.Completed -= handler;
    }

    [Fact]
    public async Task ProbeAsync_NonMagnetNonFile_Throws() =>
        await Assert.ThrowsAsync<NotSupportedException>(() => _engine.ProbeAsync("not-a-valid-source"));

    [Fact]
    public async Task ProbeAsync_InvalidMagnet_Throws() =>
        await Assert.ThrowsAsync<FormatException>(() => _engine.ProbeAsync("magnet:?garbage"));

    [Fact]
    public async Task AddAsync_InvalidMagnet_Throws()
    {
        var item = new DownloadItem { SourceUrl = "magnet:?garbage", DestinationDirectory = _cacheDir };
        await Assert.ThrowsAsync<FormatException>(() => _engine.AddAsync(item));
    }

    [Fact]
    public async Task AddAsync_NonExistentTorrentFile_Throws()
    {
        var item = new DownloadItem { SourceUrl = @"C:\nonexistent\torrent-file.torrent", DestinationDirectory = _cacheDir };
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => _engine.AddAsync(item));
    }
}
