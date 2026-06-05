using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using o_down.Core.Abstractions;
using o_down.Core.Models;
using o_down.Data;
using o_down.Infrastructure;
using Xunit;

namespace o_down.Infrastructure.Tests;

public class BandwidthProfileServiceTests : IDisposable
{
    private readonly OdownDbContext _db;
    private readonly BandwidthProfileService _service;
    private readonly List<FakeEngine> _engines;

    public BandwidthProfileServiceTests()
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection("Filename=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<OdownDbContext>()
            .UseSqlite(conn)
            .Options;
        _db = new OdownDbContext(options);
        _db.Database.EnsureCreated();

        _engines = new List<FakeEngine> { new(), new() };
        _service = new BandwidthProfileService(_db, _engines, NullLogger<BandwidthProfileService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetAllAsync_ReturnsBuiltInProfiles_AndPersistsThem()
    {
        var all = await _service.GetAllAsync();
        Assert.Contains(all, p => p.IsBuiltIn && p.Id == BuiltInBandwidthProfiles.Unlimited.Id);
        Assert.Contains(all, p => p.IsBuiltIn && p.Id == BuiltInBandwidthProfiles.Medium5MB.Id);
    }

    [Fact]
    public async Task AddAsync_PersistsUserProfile_AndReturnsIt()
    {
        var p = await _service.AddAsync("My Custom", 512 * 1024, 256 * 1024);
        Assert.False(p.IsBuiltIn);
        Assert.Equal(512 * 1024, p.MaxDownloadBytesPerSecond);
        Assert.True(await _db.BandwidthProfiles.AnyAsync(x => x.Id == p.Id));
    }

    [Fact]
    public async Task SetActiveAsync_AppliesLimitToAllEngines()
    {
        var profile = BuiltInBandwidthProfiles.Light1MB;
        await _service.SetActiveAsync(profile.Id);

        Assert.All(_engines, e => Assert.Equal(1024L * 1024, e.LastGlobalLimit));
        Assert.All(_engines, e => Assert.NotEmpty(e.PerDownloadCalls));
    }

    [Fact]
    public async Task SetActiveAsync_Unlimited_ResetsGlobalLimit()
    {
        await _service.SetActiveAsync(BuiltInBandwidthProfiles.Light1MB.Id);
        await _service.SetActiveAsync(BuiltInBandwidthProfiles.Unlimited.Id);

        Assert.All(_engines, e => Assert.Null(e.LastGlobalLimit));
    }

    [Fact]
    public async Task RemoveAsync_OfBuiltIn_IsNoOp()
    {
        await _service.RemoveAsync(BuiltInBandwidthProfiles.Unlimited.Id);
        Assert.Equal(BuiltInBandwidthProfiles.Unlimited, _service.ActiveProfile);
    }

    [Fact]
    public async Task RemoveAsync_OfUserProfile_ResetsToUnlimited()
    {
        var p = await _service.AddAsync("Temp", 1024, 512);
        await _service.SetActiveAsync(p.Id);
        Assert.Equal(p.Id, _service.ActiveProfile!.Id);

        await _service.RemoveAsync(p.Id);
        Assert.Equal(BuiltInBandwidthProfiles.Unlimited.Id, _service.ActiveProfile!.Id);
    }

    [Fact]
    public async Task ActiveProfileChanged_EventFires_OnSet()
    {
        var fired = new List<BandwidthProfile?>();
        _service.ActiveProfileChanged += (_, p) => fired.Add(p);

        await _service.SetActiveAsync(BuiltInBandwidthProfiles.Medium5MB.Id);
        Assert.Single(fired);
        Assert.Equal(BuiltInBandwidthProfiles.Medium5MB.Id, fired[0]!.Id);
    }

    [Fact]
    public async Task ApplyToActiveDownloadsAsync_CallsPerDownloadLimits()
    {
        var engine = _engines[0];
        engine.ActiveDownloads = new List<DownloadProgress>
        {
            new() { DownloadId = Guid.NewGuid(), State = DownloadState.Running },
            new() { DownloadId = Guid.NewGuid(), State = DownloadState.Running }
        };

        await _service.ApplyToActiveDownloadsAsync(BuiltInBandwidthProfiles.Fast10MB);
        Assert.Equal(2, engine.PerDownloadCalls.Count);
        Assert.All(engine.PerDownloadCalls, c => Assert.Equal(10L * 1024 * 1024, c.MaxDownload));
        Assert.All(engine.PerDownloadCalls, c => Assert.Equal(5L * 1024 * 1024, c.MaxUpload));
    }

    private sealed class FakeEngine : IDownloadEngine
    {
        public string Name => "fake";
        public DownloadKind Kind => DownloadKind.Http;
        public bool IsAvailable => true;
        public List<DownloadProgress> ActiveDownloads { get; set; } = new();
        public List<(string Handle, long? MaxDownload, long? MaxUpload)> PerDownloadCalls { get; } = new();
        public long? LastGlobalLimit { get; private set; }

#pragma warning disable CS0067
        public event EventHandler<DownloadProgress>? ProgressChanged;
        public event EventHandler<DownloadProgress>? Completed;
#pragma warning restore CS0067

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> AddAsync(DownloadItem item, CancellationToken ct = default) => Task.FromResult(Guid.NewGuid().ToString());
        public Task PauseAsync(string engineHandle, CancellationToken ct = default) => Task.CompletedTask;
        public Task ResumeAsync(string engineHandle, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(string engineHandle, bool deleteFiles, CancellationToken ct = default) => Task.CompletedTask;
        public Task ForceRemoveAsync(string engineHandle, CancellationToken ct = default) => Task.CompletedTask;
        public Task PurgeCompletedResultsAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task SetBandwidthLimitAsync(long? bytesPerSecond, CancellationToken ct = default)
        {
            LastGlobalLimit = bytesPerSecond;
            return Task.CompletedTask;
        }

        public Task SetDownloadLimitsAsync(string engineHandle, long? maxDownloadBytesPerSecond, long? maxUploadBytesPerSecond, CancellationToken ct = default)
        {
            PerDownloadCalls.Add((engineHandle, maxDownloadBytesPerSecond, maxUploadBytesPerSecond));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DownloadProgress>> QueryAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DownloadProgress>>(ActiveDownloads);
        public Task<DownloadProgress?> QueryAsync(string engineHandle, CancellationToken ct = default) => Task.FromResult<DownloadProgress?>(null);
    }
}
