using o_down.Core.Models;

namespace o_down.Core.Abstractions;

public sealed class TorrentMetadata
{
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
    public string? InfoHash { get; set; }
    public IReadOnlyList<TorrentFile> Files { get; set; } = Array.Empty<TorrentFile>();
    public int PieceCount { get; set; }
    public int PieceLength { get; set; }
}

public sealed class TorrentFile
{
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
}

public interface ITorrentEngine : IDownloadEngine
{
    Task<TorrentMetadata> ProbeAsync(string source, CancellationToken ct = default);
    Task SetSequentialAsync(string engineHandle, bool sequential, CancellationToken ct = default);
    IReadOnlyList<string> GetActiveTorrents();
}
