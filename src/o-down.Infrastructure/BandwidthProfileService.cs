using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using o_down.Core.Abstractions;
using o_down.Core.Models;
using o_down.Data;

namespace o_down.Infrastructure;

public interface IBandwidthProfileService
{
    IReadOnlyList<BandwidthProfile> Profiles { get; }
    BandwidthProfile? ActiveProfile { get; }
    event EventHandler<BandwidthProfile?>? ActiveProfileChanged;
    Task<IReadOnlyList<BandwidthProfile>> GetAllAsync(CancellationToken ct = default);
    Task SetActiveAsync(Guid profileId, CancellationToken ct = default);
    Task<BandwidthProfile> AddAsync(string name, long? maxDownload, long? maxUpload, CancellationToken ct = default);
    Task UpdateAsync(BandwidthProfile profile, CancellationToken ct = default);
    Task RemoveAsync(Guid profileId, CancellationToken ct = default);
    Task ApplyToAllEnginesAsync(BandwidthProfile? profile, CancellationToken ct = default);
    Task ApplyToActiveDownloadsAsync(BandwidthProfile profile, CancellationToken ct = default);
}

public sealed class BandwidthProfileService : IBandwidthProfileService
{
    private readonly OdownDbContext _db;
    private readonly IEnumerable<IDownloadEngine> _engines;
    private readonly ILogger<BandwidthProfileService>? _logger;

    public BandwidthProfileService(
        OdownDbContext db,
        IEnumerable<IDownloadEngine> engines,
        ILogger<BandwidthProfileService>? logger = null)
    {
        _db = db;
        _engines = engines;
        _logger = logger;
    }

    public IReadOnlyList<BandwidthProfile> Profiles => BuiltInBandwidthProfiles.All;
    public BandwidthProfile? ActiveProfile { get; private set; } = BuiltInBandwidthProfiles.Unlimited;
    public event EventHandler<BandwidthProfile?>? ActiveProfileChanged;

    public async Task<IReadOnlyList<BandwidthProfile>> GetAllAsync(CancellationToken ct = default)
    {
        var userProfiles = await _db.BandwidthProfiles
            .AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return BuiltInBandwidthProfiles.All
            .Concat(userProfiles)
            .OrderBy(x => x.SortOrder)
            .ToList();
    }

    public async Task SetActiveAsync(Guid profileId, CancellationToken ct = default)
    {
        var profile = (await GetAllAsync(ct).ConfigureAwait(false))
            .FirstOrDefault(p => p.Id == profileId);
        if (profile is null) return;

        ActiveProfile = profile;
        ActiveProfileChanged?.Invoke(this, profile);
        await ApplyToAllEnginesAsync(profile, ct).ConfigureAwait(false);
        _logger?.LogInformation("Bandwidth profile changed to {Name} (DL={Dl} UL={Ul})",
            profile.Name, profile.MaxDownloadBytesPerSecond, profile.MaxUploadBytesPerSecond);
    }

    public async Task<BandwidthProfile> AddAsync(string name, long? maxDownload, long? maxUpload, CancellationToken ct = default)
    {
        var maxSort = await _db.BandwidthProfiles.MaxAsync(x => (int?)x.SortOrder, ct).ConfigureAwait(false) ?? BuiltInBandwidthProfiles.All.Count;
        var profile = new BandwidthProfile
        {
            Name = name,
            MaxDownloadBytesPerSecond = maxDownload,
            MaxUploadBytesPerSecond = maxUpload,
            IsBuiltIn = false,
            SortOrder = maxSort + 1
        };
        _db.BandwidthProfiles.Add(profile);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return profile;
    }

    public async Task UpdateAsync(BandwidthProfile profile, CancellationToken ct = default)
    {
        if (profile.IsBuiltIn) return; // built-ins are immutable
        var existing = await _db.BandwidthProfiles.FindAsync(new object?[] { profile.Id }, ct).ConfigureAwait(false);
        if (existing is null) return;
        existing.Name = profile.Name;
        existing.MaxDownloadBytesPerSecond = profile.MaxDownloadBytesPerSecond;
        existing.MaxUploadBytesPerSecond = profile.MaxUploadBytesPerSecond;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (ActiveProfile?.Id == profile.Id)
            await ApplyToAllEnginesAsync(profile, ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Guid profileId, CancellationToken ct = default)
    {
        if (BuiltInBandwidthProfiles.All.Any(p => p.Id == profileId)) return;
        var profile = await _db.BandwidthProfiles.FindAsync(new object?[] { profileId }, ct).ConfigureAwait(false);
        if (profile is null) return;
        _db.BandwidthProfiles.Remove(profile);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (ActiveProfile?.Id == profileId)
        {
            await SetActiveAsync(BuiltInBandwidthProfiles.Unlimited.Id, ct).ConfigureAwait(false);
        }
    }

    public async Task ApplyToAllEnginesAsync(BandwidthProfile? profile, CancellationToken ct = default)
    {
        var p = profile ?? BuiltInBandwidthProfiles.Unlimited;
        var tasks = new List<Task>();
        foreach (var engine in _engines)
        {
            try { tasks.Add(engine.SetBandwidthLimitAsync(p.MaxDownloadBytesPerSecond, ct)); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Engine {Engine} rejected global limit", engine.Name); }
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);

        if (!p.IsUnlimited)
            await ApplyToActiveDownloadsAsync(p, ct).ConfigureAwait(false);
    }

    public async Task ApplyToActiveDownloadsAsync(BandwidthProfile profile, CancellationToken ct = default)
    {
        foreach (var engine in _engines)
        {
            IReadOnlyList<DownloadProgress> active;
            try { active = await engine.QueryAllAsync(ct).ConfigureAwait(false); }
            catch (Exception ex) { _logger?.LogDebug(ex, "QueryAllAsync failed for {Engine}", engine.Name); continue; }
            foreach (var p in active)
            {
                if (p.DownloadId == Guid.Empty) continue;
                var handle = p.DownloadId.ToString();
                try
                {
                    await engine.SetDownloadLimitsAsync(handle, profile.MaxDownloadBytesPerSecond, profile.MaxUploadBytesPerSecond, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "SetDownloadLimitsAsync failed for {Engine}/{Handle}", engine.Name, handle);
                }
            }
        }
    }
}
