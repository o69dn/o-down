using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using o_down.Core.Abstractions;
using o_down.Core.Models;
using o_down.Core.Pipeline;
using Microsoft.Extensions.Logging;

namespace o_down.Engines.Media;

public sealed class YtDlpMediaExtractor : IMediaExtractor
{
    private readonly string _ytDlpPath;
    private readonly string _ffmpegPath;
    private readonly ILogger<YtDlpMediaExtractor>? _logger;

    public YtDlpMediaExtractor(string ytDlpPath, string ffmpegPath, ILogger<YtDlpMediaExtractor>? logger = null)
    {
        _ytDlpPath = ytDlpPath;
        _ffmpegPath = ffmpegPath;
        _logger = logger;
    }

    public string Name => "yt-dlp";
    public bool IsAvailable => File.Exists(_ytDlpPath);

    public Task<bool> CanHandleAsync(string url, CancellationToken ct = default)
    {
        // yt-dlp handles an enormous set of sites; for the simple classifier, any http(s) URL is a candidate.
        return Task.FromResult(Uri.TryCreate(url, UriKind.Absolute, out _));
    }

    public async Task<MediaProbe> ProbeAsync(string url, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ytDlpPath,
            Arguments = $"--no-warnings --no-progress --skip-download --dump-single-json -J \"{url}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };
        // yt-dlp picks up ffmpeg from PATH; ensure our bundled ffmpeg wins.
        if (File.Exists(_ffmpegPath))
        {
            var ffmpegDir = Path.GetDirectoryName(_ffmpegPath) ?? string.Empty;
            var currentPath = psi.EnvironmentVariables["PATH"] ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            psi.EnvironmentVariables["PATH"] = ffmpegDir + Path.PathSeparator + currentPath;
        }

        using var p = Process.Start(psi)!;
        var json = await p.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        await p.WaitForExitAsync(ct).ConfigureAwait(false);
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"yt-dlp probe failed with code {p.ExitCode}");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var formats = new List<MediaFormat>();
        if (root.TryGetProperty("formats", out var formatsArray))
        {
            foreach (var f in formatsArray.EnumerateArray())
            {
                var id = f.TryGetProperty("format_id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty;
                var ext = f.TryGetProperty("ext", out var extEl) ? extEl.GetString() ?? string.Empty : string.Empty;
                var height = f.TryGetProperty("height", out var hEl) && hEl.ValueKind == JsonValueKind.Number ? hEl.GetInt32() : (int?)null;
                var vcodec = f.TryGetProperty("vcodec", out var vEl) ? vEl.GetString() : null;
                var acodec = f.TryGetProperty("acodec", out var aEl) ? aEl.GetString() : null;
                var size = f.TryGetProperty("filesize", out var sEl) && sEl.ValueKind == JsonValueKind.Number ? sEl.GetInt64() : (long?)null;
                formats.Add(new MediaFormat
                {
                    Id = id,
                    Extension = ext,
                    Resolution = height.HasValue ? $"{height}p" : null,
                    VideoCodec = vcodec == "none" ? null : vcodec,
                    AudioCodec = acodec == "none" ? null : acodec,
                    ApproximateSize = size,
                    Container = ext
                });
            }
        }

        var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "Unknown" : "Unknown";
        var uploader = root.TryGetProperty("uploader", out var u) ? u.GetString() : null;
        var thumb = root.TryGetProperty("thumbnail", out var th) ? th.GetString() : null;
        var dur = root.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? TimeSpan.FromSeconds(d.GetDouble()) : (TimeSpan?)null;
        var site = root.TryGetProperty("extractor", out var x) ? x.GetString() : null;

        return new MediaProbe
        {
            Url = url,
            Title = title,
            Uploader = uploader,
            Duration = dur,
            Thumbnail = thumb,
            Site = site,
            Formats = formats
        };
    }

    public async Task<string> DownloadAsync(DownloadItem item, MediaFormat format, string outputTemplate, Action<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(item.DestinationDirectory);
        var args = new List<string>
        {
            "--no-warnings",
            "--no-part",
            "-f", format.Id,
            "-o", $"\"{outputTemplate}\"",
            "--ffmpeg-location", $"\"{_ffmpegPath}\"",
            "--newline",
            "--no-mtime",
            "--no-playlist"
        };
        if (item.MediaAudioOnly)
        {
            args.Add("-x");
            if (!string.IsNullOrEmpty(item.MediaAudioFormat))
                args.AddRange(new[] { "--audio-format", item.MediaAudioFormat });
        }
        if (item.MediaWriteSubtitles)
        {
            args.Add("--write-subs");
            if (!string.IsNullOrEmpty(item.MediaSubtitleLanguages))
                args.AddRange(new[] { "--sub-langs", item.MediaSubtitleLanguages });
        }
        if (item.MediaEmbedSubtitles)
            args.Add("--embed-subs");
        if (!string.IsNullOrEmpty(item.MediaSponsorblockRemove))
            args.AddRange(new[] { "--sponsorblock-remove", item.MediaSponsorblockRemove });
        if (item.ReferrerUrl is { Length: > 0 } refUrl) args.AddRange(new[] { "--referer", $"\"{refUrl}\"" });
        args.Add($"\"{item.SourceUrl}\"");
        var psi = new ProcessStartInfo
        {
            FileName = _ytDlpPath,
            Arguments = string.Join(' ', args),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        _logger?.LogInformation("yt-dlp starting: {Args}", psi.Arguments);
        using var p = Process.Start(psi)!;
        if (progress is not null)
        {
            var stderrTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await p.StandardError.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
                {
                    var ev = YtDlpProgressParser.Parse(line);
                    if (ev is null) continue;
                    if (ev.Kind is YtDlpEventKind.Progress or YtDlpEventKind.Completed)
                    {
                        progress(new DownloadProgress
                        {
                            ReceivedBytes = ev.ReceivedBytes ?? 0,
                            TotalBytes = ev.TotalBytes,
                            SpeedBytesPerSecond = ev.SpeedBytesPerSecond ?? 0,
                            Eta = ev.Eta
                        });
                    }
                }
            }, ct);
            var stdoutTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await p.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false)) is not null) { }
            }, ct);
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            try { await stderrTask.ConfigureAwait(false); } catch { }
            try { await stdoutTask.ConfigureAwait(false); } catch { }
        }
        else
        {
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"yt-dlp download failed (exit {p.ExitCode})");
        return item.DestinationDirectory;
    }
}
