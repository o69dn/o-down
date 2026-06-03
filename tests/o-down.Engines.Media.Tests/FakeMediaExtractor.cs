using o_down.Core.Abstractions;
using o_down.Core.Models;

namespace o_down.Engines.Media.Tests;

/// <summary>
/// Configurable in-process <see cref="IMediaExtractor"/> for testing the
/// <see cref="MediaDownloadEngine"/> without spawning yt-dlp. Tests can
/// pre-program probe results, completion delays, and a sequence of
/// progress events to drive the engine's event surface.
/// </summary>
public sealed class FakeMediaExtractor : IMediaExtractor
{
    public List<MediaProbe> Probes { get; } = new();
    public List<DownloadItem> Downloads { get; } = new();
    public List<(DownloadItem item, MediaFormat format, string template)> DownloadCalls { get; } = new();
    public Func<DownloadItem, IReadOnlyList<DownloadProgress>>? ProgressSequence { get; set; }
    public TimeSpan DownloadDelay { get; set; } = TimeSpan.FromMilliseconds(50);
    public Exception? ThrowOnDownload { get; set; }

    public bool IsAvailable { get; set; } = true;
    public string Name => "fake";

    public Task<bool> CanHandleAsync(string url, CancellationToken ct = default) => Task.FromResult(Uri.TryCreate(url, UriKind.Absolute, out _));

    public Task<MediaProbe> ProbeAsync(string url, CancellationToken ct = default)
    {
        Probes.Add(new MediaProbe { Url = url, Title = "Test Video " + Probes.Count, Site = "fake" });
        return Task.FromResult(Probes[^1]);
    }

    public async Task<string> DownloadAsync(DownloadItem item, MediaFormat format, string outputTemplate, Action<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        DownloadCalls.Add((item, format, outputTemplate));
        Downloads.Add(item);

        if (ProgressSequence is not null)
        {
            foreach (var p in ProgressSequence(item))
            {
                progress?.Invoke(p);
            }
        }

        if (DownloadDelay > TimeSpan.Zero)
            await Task.Delay(DownloadDelay, ct).ConfigureAwait(false);

        if (ThrowOnDownload is not null)
            throw ThrowOnDownload;

        // Create a tiny file at the expected path so the engine could inspect it.
        try
        {
            var dir = Path.GetDirectoryName(outputTemplate);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var file = outputTemplate.Contains("%(ext)s", StringComparison.Ordinal)
                ? Path.Combine(dir ?? ".", item.FilenameHint + ".mp4")
                : outputTemplate;
            await File.WriteAllBytesAsync(file, new byte[] { 0x00, 0x01, 0x02, 0x03 }, ct).ConfigureAwait(false);
        }
        catch
        {
        }
        return item.DestinationDirectory;
    }
}
