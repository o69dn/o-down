using o_down.Core.Models;

namespace o_down.Core.Abstractions;

public sealed class DownloadProgress
{
    public Guid DownloadId { get; set; }
    public long ReceivedBytes { get; set; }
    public long? TotalBytes { get; set; }
    public long SpeedBytesPerSecond { get; set; }
    public int Connections { get; set; }
    public string? Eta { get; set; }
    public DownloadState State { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public double Progress => TotalBytes is > 0 ? (double)ReceivedBytes / TotalBytes.Value : 0.0;
}

public interface IDownloadEngine
{
    string Name { get; }
    DownloadKind Kind { get; }
    bool IsAvailable { get; }
    Task InitializeAsync(CancellationToken ct = default);
    Task ShutdownAsync(CancellationToken ct = default);
    Task<string> AddAsync(DownloadItem item, CancellationToken ct = default);
    Task PauseAsync(string engineHandle, CancellationToken ct = default);
    Task ResumeAsync(string engineHandle, CancellationToken ct = default);
    Task RemoveAsync(string engineHandle, bool deleteFiles, CancellationToken ct = default);
    Task ForceRemoveAsync(string engineHandle, CancellationToken ct = default);
    Task PurgeCompletedResultsAsync(CancellationToken ct = default);
    Task SetBandwidthLimitAsync(long? bytesPerSecond, CancellationToken ct = default);
    Task SetDownloadLimitsAsync(string engineHandle, long? maxDownloadBytesPerSecond, long? maxUploadBytesPerSecond, CancellationToken ct = default);
    Task<IReadOnlyList<DownloadProgress>> QueryAllAsync(CancellationToken ct = default);
    Task<DownloadProgress?> QueryAsync(string engineHandle, CancellationToken ct = default);
    event EventHandler<DownloadProgress>? ProgressChanged;
    event EventHandler<DownloadProgress>? Completed;
}
