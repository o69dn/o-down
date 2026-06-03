using o_down.Core.Models;
using o_down.Core.Pipeline;
using o_down.Update;
using Xunit;

namespace o_down.Update.Tests;

public class UpdateCheckSchedulerTests
{
    [Fact]
    public async Task CheckNowAsync_Skips_When_AutoUpdateDisabled()
    {
        var feed = new RecordingFeed();
        var settings = new InMemorySettingsStore { Current = new AppSettings { AutoUpdateEnabled = false, UpdateChannel = "stable" } };
        var svc = new UpdateService(feed, "C:\\app", new Version(1, 0, 0));
        var scheduler = new UpdateCheckScheduler(svc, settings, TimeSpan.FromMinutes(1));

        var result = await scheduler.CheckNowAsync();

        Assert.False(result.HasUpdate);
        Assert.Equal("Auto-update disabled", result.Error);
        Assert.Equal(0, feed.CallCount);
    }

    [Fact]
    public async Task CheckNowAsync_CallsFeed_With_CurrentChannel()
    {
        var feed = new RecordingFeed(new UpdateManifest { Version = "9.9.9", DownloadUrl = "x", Sha256 = "" });
        var settings = new InMemorySettingsStore { Current = new AppSettings { AutoUpdateEnabled = true, UpdateChannel = "beta" } };
        var svc = new UpdateService(feed, "C:\\app", new Version(1, 0, 0));
        var scheduler = new UpdateCheckScheduler(svc, settings, TimeSpan.FromMinutes(1));

        var result = await scheduler.CheckNowAsync();

        Assert.True(result.HasUpdate);
        Assert.Equal("9.9.9", result.Manifest!.Version);
        Assert.Equal("beta", feed.LastChannel);
    }

    [Fact]
    public async Task CheckNowAsync_RaisesUpdateAvailable_When_HasUpdate()
    {
        var feed = new RecordingFeed(new UpdateManifest { Version = "2.0.0", DownloadUrl = "x", Sha256 = "" });
        var settings = new InMemorySettingsStore { Current = new AppSettings { AutoUpdateEnabled = true, UpdateChannel = "stable" } };
        var svc = new UpdateService(feed, "C:\\app", new Version(1, 0, 0));
        var scheduler = new UpdateCheckScheduler(svc, settings, TimeSpan.FromMinutes(1));

        UpdateCheckResult? captured = null;
        scheduler.UpdateAvailable += (_, r) => captured = r;

        await scheduler.CheckNowAsync();
        Assert.NotNull(captured);
        Assert.True(captured!.HasUpdate);
    }

    [Fact]
    public async Task CheckNowAsync_RaisesCheckCompleted_Even_WhenNoUpdate()
    {
        var feed = new RecordingFeed(new UpdateManifest { Version = "1.0.0", DownloadUrl = "x", Sha256 = "" });
        var settings = new InMemorySettingsStore { Current = new AppSettings { AutoUpdateEnabled = true, UpdateChannel = "stable" } };
        var svc = new UpdateService(feed, "C:\\app", new Version(1, 0, 0));
        var scheduler = new UpdateCheckScheduler(svc, settings, TimeSpan.FromMinutes(1));

        var fired = 0;
        scheduler.CheckCompleted += (_, _) => Interlocked.Increment(ref fired);

        await scheduler.CheckNowAsync();
        Assert.Equal(1, fired);
    }

    [Fact]
    public async Task Start_ThenStop_RunsAtLeastOnce()
    {
        var feed = new RecordingFeed(new UpdateManifest { Version = "1.0.0", DownloadUrl = "x", Sha256 = "" });
        var settings = new InMemorySettingsStore { Current = new AppSettings { AutoUpdateEnabled = true, UpdateChannel = "stable" } };
        var svc = new UpdateService(feed, "C:\\app", new Version(1, 0, 0));
        var scheduler = new UpdateCheckScheduler(svc, settings, TimeSpan.FromMilliseconds(50));

        scheduler.Start();
        Assert.True(scheduler.IsRunning);
        await Task.Delay(200);
        await scheduler.StopAsync();
        Assert.False(scheduler.IsRunning);
        Assert.True(feed.CallCount >= 1, $"Expected at least 1 call, got {feed.CallCount}");
    }

    [Fact]
    public async Task Start_Is_Idempotent()
    {
        var feed = new RecordingFeed();
        var settings = new InMemorySettingsStore { Current = new AppSettings { AutoUpdateEnabled = true, UpdateChannel = "stable" } };
        var svc = new UpdateService(feed, "C:\\app", new Version(1, 0, 0));
        var scheduler = new UpdateCheckScheduler(svc, settings, TimeSpan.FromMinutes(10));

        scheduler.Start();
        await Task.Delay(50);
        var callsAfterFirstStart = feed.CallCount;
        Assert.Equal(1, callsAfterFirstStart);

        scheduler.Start();
        scheduler.Start();
        await Task.Delay(100);
        await scheduler.StopAsync();

        Assert.Equal(callsAfterFirstStart, feed.CallCount);
    }

    [Fact]
    public async Task LastResult_IsSet_AfterCheckNow()
    {
        var feed = new RecordingFeed(new UpdateManifest { Version = "2.0.0", DownloadUrl = "x", Sha256 = "" });
        var settings = new InMemorySettingsStore { Current = new AppSettings { AutoUpdateEnabled = true, UpdateChannel = "stable" } };
        var svc = new UpdateService(feed, "C:\\app", new Version(1, 0, 0));
        var scheduler = new UpdateCheckScheduler(svc, settings, TimeSpan.FromMinutes(1));

        Assert.Null(scheduler.LastResult);
        await scheduler.CheckNowAsync();
        Assert.NotNull(scheduler.LastResult);
        Assert.True(scheduler.LastResult!.HasUpdate);
    }

    [Fact]
    public async Task CheckNowAsync_ReadsLatestSettings_OnEachCall()
    {
        var feed = new RecordingFeed(new UpdateManifest { Version = "1.0.0", DownloadUrl = "x", Sha256 = "" });
        var settings = new InMemorySettingsStore { Current = new AppSettings { AutoUpdateEnabled = true, UpdateChannel = "stable" } };
        var svc = new UpdateService(feed, "C:\\app", new Version(1, 0, 0));
        var scheduler = new UpdateCheckScheduler(svc, settings, TimeSpan.FromMinutes(1));

        await scheduler.CheckNowAsync();
        Assert.Equal("stable", feed.LastChannel);

        settings.Current = new AppSettings { AutoUpdateEnabled = true, UpdateChannel = "insider" };
        await scheduler.CheckNowAsync();
        Assert.Equal("insider", feed.LastChannel);
    }

    [Fact]
    public async Task BackgroundLoop_Skips_WhenUserDisablesAutoUpdateMidLoop()
    {
        var feed = new RecordingFeed();
        var settings = new InMemorySettingsStore { Current = new AppSettings { AutoUpdateEnabled = true, UpdateChannel = "stable" } };
        var svc = new UpdateService(feed, "C:\\app", new Version(1, 0, 0));
        var scheduler = new UpdateCheckScheduler(svc, settings, TimeSpan.FromMilliseconds(30));

        scheduler.Start();
        await Task.Delay(120);
        var callsWhileEnabled = feed.CallCount;
        Assert.True(callsWhileEnabled >= 1);

        settings.Current = new AppSettings { AutoUpdateEnabled = false, UpdateChannel = "stable" };
        await Task.Delay(200);
        await scheduler.StopAsync();

        Assert.Equal(callsWhileEnabled, feed.CallCount);
    }

    private sealed class RecordingFeed : IUpdateFeed
    {
        private readonly UpdateManifest? _manifest;
        public int CallCount;
        public string? LastChannel;
        public RecordingFeed(UpdateManifest? manifest = null) => _manifest = manifest;

        public Task<UpdateManifest?> GetLatestAsync(string channel, CancellationToken ct = default)
        {
            Interlocked.Increment(ref CallCount);
            LastChannel = channel;
            return Task.FromResult(_manifest);
        }
    }

    private sealed class InMemorySettingsStore : IAppSettingsStore
    {
        public AppSettings Current { get; set; } = AppSettings.Default();
        public Task<AppSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(Current);
        public Task SaveAsync(AppSettings settings, CancellationToken ct = default) { Current = settings; return Task.CompletedTask; }
        public void Reload() { }
    }
}
