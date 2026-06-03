using o_down.Core.Models;

namespace o_down.Core.Abstractions;

public interface IMediaExtractor
{
    string Name { get; }
    bool IsAvailable { get; }
    Task<bool> CanHandleAsync(string url, CancellationToken ct = default);
    Task<MediaProbe> ProbeAsync(string url, CancellationToken ct = default);
    Task<string> DownloadAsync(DownloadItem item, MediaFormat format, string outputTemplate, Action<DownloadProgress>? progress = null, CancellationToken ct = default);
}
