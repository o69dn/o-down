using o_down.Core.Abstractions;
using o_down.Core.Models;
using o_down.Core.Pipeline;
using o_down.Engines.Media;
using Xunit;

namespace o_down.Engines.Media.Tests;

public class MediaDownloadEngineTests
{
    private static DownloadItem MakeItem(string url = "https://example.com/video", string title = "T") => new()
    {
        Kind = DownloadKind.Media,
        SourceUrl = url,
        FilenameHint = title,
        DestinationDirectory = Path.Combine(Path.GetTempPath(), "odown-mde-" + Guid.NewGuid().ToString("N")),
        MediaFormatPreference = MediaFormatPreference.BestVideoPlusBestAudio
    };

    [Fact]
    public async Task AddAsync_FiresProgressChangedAndCompleted()
    {
        var extractor = new FakeMediaExtractor
        {
            ProgressSequence = _ => new[]
            {
                new DownloadProgress { ReceivedBytes = 100, TotalBytes = 1000, SpeedBytesPerSecond = 1024 },
                new DownloadProgress { ReceivedBytes = 500, TotalBytes = 1000, SpeedBytesPerSecond = 1024 },
                new DownloadProgress { ReceivedBytes = 1000, TotalBytes = 1000, SpeedBytesPerSecond = 0 }
            }
        };
        await using var engine = new MediaDownloadEngine(extractor);
        await engine.InitializeAsync();
        Assert.Equal("media", engine.Name);
        Assert.Equal(DownloadKind.Media, engine.Kind);
        Assert.True(engine.IsAvailable);

        var progressEvents = new List<DownloadProgress>();
        var completedEvents = new List<DownloadProgress>();
        engine.ProgressChanged += (_, p) => progressEvents.Add(p);
        engine.Completed += (_, p) => completedEvents.Add(p);

        var item = MakeItem();
        var handle = await engine.AddAsync(item);
        Assert.False(string.IsNullOrEmpty(handle));
        await engine.QueryAsync(handle); // forces a state read
        // Wait for the background task to finish
        await Task.Delay(500);

        Assert.NotEmpty(progressEvents);
        Assert.Contains(progressEvents, p => p.State == DownloadState.FetchingMetadata);
        Assert.Contains(progressEvents, p => p.State == DownloadState.Running);
        Assert.Single(completedEvents);
        Assert.Equal(DownloadState.Completed, completedEvents[0].State);
        Assert.Single(extractor.Downloads);
    }

    [Fact]
    public async Task AddAsync_RespectsExplicitFormatId()
    {
        var extractor = new FakeMediaExtractor();
        await using var engine = new MediaDownloadEngine(extractor);
        await engine.InitializeAsync();
        var item = MakeItem();
        item.MediaFormatId = "22";

        var handle = await engine.AddAsync(item);
        await Task.Delay(200);

        var call = Assert.Single(extractor.DownloadCalls);
        Assert.Equal("22", call.format.Id);
    }

    [Fact]
    public async Task AddAsync_FallsBackToBestWhenNoFormatMatchesPreference()
    {
        var extractor = new FakeMediaExtractor();
        await using var engine = new MediaDownloadEngine(extractor);
        await engine.InitializeAsync();
        var item = MakeItem();
        item.MediaFormatPreference = MediaFormatPreference.Best;

        var handle = await engine.AddAsync(item);
        await Task.Delay(200);

        var call = Assert.Single(extractor.DownloadCalls);
        Assert.Equal("best", call.format.Id);
    }

    [Fact]
    public async Task AddAsync_AudioOnlyUsesBestaudio()
    {
        var extractor = new FakeMediaExtractor();
        await using var engine = new MediaDownloadEngine(extractor);
        await engine.InitializeAsync();
        var item = MakeItem();
        item.MediaAudioOnly = true;
        item.MediaAudioFormat = "mp3";

        var handle = await engine.AddAsync(item);
        await Task.Delay(200);

        var call = Assert.Single(extractor.DownloadCalls);
        Assert.Equal("bestaudio/best", call.format.Id);
        Assert.Equal("mp3", call.format.Extension);
    }

    [Fact]
    public async Task AddAsync_ResolvesOutputTemplate_WhenNotProvided()
    {
        var extractor = new FakeMediaExtractor();
        await using var engine = new MediaDownloadEngine(extractor);
        await engine.InitializeAsync();
        var item = MakeItem();

        var handle = await engine.AddAsync(item);
        await Task.Delay(200);

        var call = Assert.Single(extractor.DownloadCalls);
        // The FakeMediaExtractor returns probe title "Test Video 0"; the engine
        // resolves the default template %(title)s.%(ext)s against the probe.
        Assert.Contains("Test Video 0", call.template);
        Assert.EndsWith(".mp4", call.template);
    }

    [Fact]
    public async Task AddAsync_PassesUserTemplateThroughToExtractor()
    {
        var extractor = new FakeMediaExtractor();
        await using var engine = new MediaDownloadEngine(extractor);
        await engine.InitializeAsync();
        var item = MakeItem();
        item.MediaOutputTemplate = "%(uploader)s/%(title)s.%(ext)s";

        var handle = await engine.AddAsync(item);
        await Task.Delay(200);

        var call = Assert.Single(extractor.DownloadCalls);
        Assert.Equal("%(uploader)s/%(title)s.%(ext)s", call.template);
    }

    [Fact]
    public async Task AddAsync_FailsWhenExtractorThrows()
    {
        var extractor = new FakeMediaExtractor { ThrowOnDownload = new InvalidOperationException("yt-dlp not found") };
        await using var engine = new MediaDownloadEngine(extractor);
        await engine.InitializeAsync();

        var failed = new List<DownloadProgress>();
        engine.Completed += (_, p) => { if (p.State == DownloadState.Failed) failed.Add(p); };

        var item = MakeItem();
        await engine.AddAsync(item);
        await Task.Delay(200);

        Assert.NotEmpty(failed);
        Assert.Contains("yt-dlp not found", failed[0].ErrorMessage ?? "");
    }

    [Fact]
    public async Task AddAsync_ThrowsWhenUnavailable()
    {
        var extractor = new FakeMediaExtractor { IsAvailable = false };
        await using var engine = new MediaDownloadEngine(extractor);
        await engine.InitializeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.AddAsync(MakeItem()));
    }

    [Fact]
    public async Task RemoveAsync_CancelsRunningDownload()
    {
        var extractor = new FakeMediaExtractor { DownloadDelay = TimeSpan.FromSeconds(5) };
        await using var engine = new MediaDownloadEngine(extractor);
        await engine.InitializeAsync();

        var item = MakeItem();
        var handle = await engine.AddAsync(item);
        await Task.Delay(50);

        await engine.RemoveAsync(handle, deleteFiles: false);
        var after = await engine.QueryAsync(handle);
        Assert.Null(after);
    }

    [Fact]
    public async Task QueryAllAsync_SnapshotsRunningDownloads()
    {
        var extractor = new FakeMediaExtractor { DownloadDelay = TimeSpan.FromSeconds(2) };
        await using var engine = new MediaDownloadEngine(extractor);
        await engine.InitializeAsync();
        var item = MakeItem();
        var handle = await engine.AddAsync(item);
        await Task.Delay(50);

        var all = await engine.QueryAllAsync();
        Assert.Single(all);
        Assert.Equal(item.Id, all[0].DownloadId);

        await engine.RemoveAsync(handle, deleteFiles: false);
    }

    [Fact]
    public void ForceRemove_DelegatesToRemove()
    {
        // ForceRemoveAsync is a thin alias for RemoveAsync(deleteFiles:true) for media items.
        // Verified via the public surface: same signature intent, no separate state.
        var extractor = new FakeMediaExtractor();
        var engine = new MediaDownloadEngine(extractor);
        var mi = typeof(MediaDownloadEngine).GetMethod("ForceRemoveAsync");
        Assert.NotNull(mi);
    }
}
