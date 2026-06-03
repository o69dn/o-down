using o_down.Core.Abstractions;
using o_down.Core.Models;

namespace o_down.Core.Pipeline;

public sealed class DownloadRouter : IDownloadRouter
{
    private readonly IReadOnlyList<IDownloadEngine> _httpEngines;
    private readonly IReadOnlyList<IDownloadEngine> _mediaEngines;
    private readonly IReadOnlyList<ITorrentEngine> _torrentEngines;
    private readonly IReadOnlyList<IMediaExtractor> _extractors;

    public DownloadRouter(
        IEnumerable<IDownloadEngine> httpEngines,
        IEnumerable<ITorrentEngine> torrentEngines,
        IEnumerable<IMediaExtractor> extractors)
    {
        _httpEngines = httpEngines.Where(e => e.Kind == DownloadKind.Http).ToList();
        _mediaEngines = httpEngines.Where(e => e.Kind == DownloadKind.Media).ToList();
        _torrentEngines = torrentEngines.ToList();
        _extractors = extractors.ToList();
    }

    public DownloadKind Classify(string url) => UrlClassifier.Classify(url);

    public Task<IDownloadEngine> PickEngineAsync(DownloadItem item, CancellationToken ct = default)
    {
        IDownloadEngine? engine = item.Kind switch
        {
            DownloadKind.Torrent => _torrentEngines.FirstOrDefault(e => e.IsAvailable),
            DownloadKind.Media => _mediaEngines.FirstOrDefault(e => e.IsAvailable)
                                  ?? _httpEngines.FirstOrDefault(e => e.IsAvailable),
            _ => _httpEngines.FirstOrDefault(e => e.IsAvailable)
        };
        return Task.FromResult(engine ?? throw new InvalidOperationException(
            $"No engine available for {item.Kind}"));
    }
}
