using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using o_down.Core.Abstractions;
using o_down.Core.Models;
using o_down.Core.Pipeline;
using o_down.Data;

namespace o_down.App.Services;

public sealed class LinkIngestService
{
    private readonly OdownDbContext _db;
    private readonly IDownloadQueue _queue;
    private readonly AutoSortEngine _sorter;
    private readonly ILogger<LinkIngestService> _logger;

    public LinkIngestService(OdownDbContext db, IDownloadQueue queue, AutoSortEngine sorter, ILogger<LinkIngestService> logger)
    {
        _db = db;
        _queue = queue;
        _sorter = sorter;
        _logger = logger;
    }

    public async Task<DownloadItem> IngestAsync(string url, string source, string? referrer = null, string? cookies = null, string? filenameHint = null, CancellationToken ct = default)
    {
        var kind = UrlClassifier.Classify(url);
        var item = new DownloadItem
        {
            Kind = kind,
            SourceUrl = url,
            ReferrerUrl = referrer,
            Cookies = cookies,
            FilenameHint = filenameHint ?? Path.GetFileName(new Uri(url).AbsolutePath),
            DestinationDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            Category = "Default",
            State = DownloadState.Queued
        };
        var rule = _sorter.Match(item);
        if (rule is not null)
        {
            item.Category = rule.Name;
            item.DestinationDirectory = Environment.ExpandEnvironmentVariables(rule.DestinationDirectory);
        }
        _db.Downloads.Add(item);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        _queue.Enqueue(item);
        _logger.LogInformation("Ingested {Kind} from {Source}: {Url}", kind, source, url);
        return item;
    }

    public async Task<IReadOnlyList<DownloadItem>> IngestBatchAsync(IEnumerable<string> urls, string source, CancellationToken ct = default)
    {
        var list = new List<DownloadItem>();
        foreach (var u in urls.SelectMany(SplitMultipleUrls))
        {
            if (string.IsNullOrWhiteSpace(u)) continue;
            if (!UrlClassifier.IsUrl(u)) continue;
            list.Add(await IngestAsync(u.Trim(), source, ct: ct).ConfigureAwait(false));
        }
        return list;
    }

    private static IEnumerable<string> SplitMultipleUrls(string text) =>
        text.Split(new[] { '\n', '\r', ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
}
