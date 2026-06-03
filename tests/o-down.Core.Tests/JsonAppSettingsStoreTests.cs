using o_down.Core.Models;
using o_down.Core.Pipeline;
using Xunit;

namespace o_down.Core.Tests;

public class JsonAppSettingsStoreTests : IDisposable
{
    private readonly string _workDir;
    private readonly string _path;

    public JsonAppSettingsStoreTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "odown-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
        _path = Path.Combine(_workDir, "settings.json");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_workDir)) Directory.Delete(_workDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task LoadAsync_ReturnsDefault_When_FileMissing()
    {
        var store = new JsonAppSettingsStore(_path);
        var loaded = await store.LoadAsync();
        Assert.Equal(5, loaded.MaxConcurrentDownloads);
        Assert.True(loaded.AutoUpdateEnabled);
        Assert.Equal("stable", loaded.UpdateChannel);
    }

    [Fact]
    public async Task SaveAsync_Then_LoadAsync_RoundTrips()
    {
        var store = new JsonAppSettingsStore(_path);
        var custom = new AppSettings
        {
            DefaultDownloadDirectory = "C:\\Downloads",
            MaxConcurrentDownloads = 3,
            ClipboardMonitorEnabled = true,
            UpdateChannel = "beta",
            MinimizeToTray = false,
        };

        await store.SaveAsync(custom);
        var store2 = new JsonAppSettingsStore(_path);
        var loaded = await store2.LoadAsync();

        Assert.Equal("C:\\Downloads", loaded.DefaultDownloadDirectory);
        Assert.Equal(3, loaded.MaxConcurrentDownloads);
        Assert.True(loaded.ClipboardMonitorEnabled);
        Assert.Equal("beta", loaded.UpdateChannel);
        Assert.False(loaded.MinimizeToTray);
    }

    [Fact]
    public async Task SaveAsync_UpdatesCurrent()
    {
        var store = new JsonAppSettingsStore(_path);
        var custom = AppSettings.Default();
        custom.MaxConcurrentDownloads = 7;
        await store.SaveAsync(custom);
        Assert.Equal(7, store.Current.MaxConcurrentDownloads);
    }

    [Fact]
    public async Task SaveAsync_IsAtomic_NoTmpFileLeftBehind()
    {
        var store = new JsonAppSettingsStore(_path);
        await store.SaveAsync(new AppSettings { MaxConcurrentDownloads = 1 });
        Assert.True(File.Exists(_path));
        Assert.False(File.Exists(_path + ".tmp"));
    }

    [Fact]
    public async Task SaveAsync_Overwrites_ExistingFile()
    {
        var store = new JsonAppSettingsStore(_path);
        await store.SaveAsync(new AppSettings { MaxConcurrentDownloads = 1 });
        await store.SaveAsync(new AppSettings { MaxConcurrentDownloads = 99 });
        var store2 = new JsonAppSettingsStore(_path);
        var loaded = await store2.LoadAsync();
        Assert.Equal(99, loaded.MaxConcurrentDownloads);
    }

    [Fact]
    public async Task LoadAsync_ReturnsDefault_When_FileCorrupt()
    {
        File.WriteAllText(_path, "{ not valid json");
        var store = new JsonAppSettingsStore(_path);
        var loaded = await store.LoadAsync();
        Assert.Equal(5, loaded.MaxConcurrentDownloads);
    }

    [Fact]
    public async Task SaveAsync_CreatesDirectoryIfMissing()
    {
        var nested = Path.Combine(_workDir, "a", "b", "c", "settings.json");
        var store = new JsonAppSettingsStore(nested);
        await store.SaveAsync(new AppSettings { MaxConcurrentDownloads = 11 });
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Reload_RereadsFromDisk()
    {
        File.WriteAllText(_path, """{"maxConcurrentDownloads":42,"updateChannel":"insider"}""");
        var store = new JsonAppSettingsStore(_path);
        store.Reload();
        Assert.Equal(42, store.Current.MaxConcurrentDownloads);
        Assert.Equal("insider", store.Current.UpdateChannel);
    }

    [Fact]
    public async Task Concurrent_SaveAsync_DoesNotCorrupt()
    {
        var store = new JsonAppSettingsStore(_path);
        var tasks = Enumerable.Range(0, 20).Select(i => store.SaveAsync(new AppSettings { MaxConcurrentDownloads = i }));
        await Task.WhenAll(tasks);

        var store2 = new JsonAppSettingsStore(_path);
        var loaded = await store2.LoadAsync();
        Assert.InRange(loaded.MaxConcurrentDownloads, 0, 19);
    }
}
