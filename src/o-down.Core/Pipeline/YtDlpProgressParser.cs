using System.Globalization;
using System.Text.RegularExpressions;
using o_down.Core.Abstractions;

namespace o_down.Core.Pipeline;

/// <summary>
/// Parses progress lines emitted by yt-dlp when invoked with
/// <c>--newline --no-progress</c>. Each line is one of:
///
///   [download]  42.0% of   10.34MiB at    1.23MiB/s ETA 00:04 (frag 5/10)
///   [download] 100% of   10.34MiB in 00:00:08 at 1.23MiB/s
///   [download] Destination: some title [abc123].mp4
///   [Merger] Merging formats into "some title [abc123].mp4"
///   [ExtractAudio] Destination: some title [abc123].mp3
///   [ffmpeg] Destination: some title [abc123].mkv
///   [info] xxxxx
///   ERROR: xxxxx
/// </summary>
public static class YtDlpProgressParser
{
    private static readonly Regex DownloadProgressRegex = new(
        @"\[download\]\s+(?<pct>\d+(?:\.\d+)?)%\s+of\s+(?<total>\S+)(?:\s+at\s+(?<speed>\S+/s))?(?:\s+ETA\s+(?<eta>\S+))?(?:\s+\((?<frag>\d+/\d+)\))?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DestinationRegex = new(
        @"\[(?:download|ExtractAudio|ffmpeg|EmbedSubtitle|VideoConvertor)\]\s+Destination:\s+(?<path>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MergerRegex = new(
        @"\[Merger\]\s+Merging formats into\s+""(?<path>.+)""\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DoneRegex = new(
        @"\[download\]\s+100%",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ErrorRegex = new(
        @"^ERROR:\s*(?<msg>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static YtDlpEvent? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var errorMatch = ErrorRegex.Match(line);
        if (errorMatch.Success)
            return new YtDlpEvent(YtDlpEventKind.Error, 0, null, null, null, null, errorMatch.Groups["msg"].Value.Trim(), null);

        var destMatch = DestinationRegex.Match(line);
        if (destMatch.Success)
            return new YtDlpEvent(YtDlpEventKind.Destination, 0, null, null, null, null, null, destMatch.Groups["path"].Value.Trim());

        var mergerMatch = MergerRegex.Match(line);
        if (mergerMatch.Success)
            return new YtDlpEvent(YtDlpEventKind.Destination, 0, null, null, null, null, null, mergerMatch.Groups["path"].Value.Trim());

        var progressMatch = DownloadProgressRegex.Match(line);
        if (progressMatch.Success)
        {
            var pct = double.Parse(progressMatch.Groups["pct"].Value, CultureInfo.InvariantCulture);
            var total = progressMatch.Groups["total"].Value;
            var speed = progressMatch.Groups["speed"].Success ? progressMatch.Groups["speed"].Value : null;
            var eta = progressMatch.Groups["eta"].Success ? progressMatch.Groups["eta"].Value : null;
            var totalBytes = TryParseSize(total);
            var speedBps = TryParseSpeed(speed);
            var receivedBytes = totalBytes.HasValue ? (long)(pct / 100.0 * totalBytes.Value) : 0;
            return new YtDlpEvent(
                DoneRegex.IsMatch(line) ? YtDlpEventKind.Completed : YtDlpEventKind.Progress,
                pct, totalBytes, receivedBytes, speedBps, eta, null, null);
        }

        return new YtDlpEvent(YtDlpEventKind.Info, 0, null, null, null, null, null, null);
    }

    public static long? TryParseSize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        text = text.Replace("iB", "").Replace("B", "");
        if (string.IsNullOrEmpty(text)) return null;
        var last = text[^1];
        double mul = 1;
        var numPart = text;
        if (last is 'K' or 'M' or 'G' or 'T')
        {
            mul = last switch { 'K' => 1024.0, 'M' => 1024.0 * 1024, 'G' => 1024.0 * 1024 * 1024, 'T' => 1024.0 * 1024 * 1024 * 1024, _ => 1 };
            numPart = text[..^1];
        }
        if (!double.TryParse(numPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return null;
        return (long)(n * mul);
    }

    public static long? TryParseSpeed(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var slash = text.IndexOf('/');
        var sizeText = slash >= 0 ? text[..slash] : text;
        return TryParseSize(sizeText);
    }
}

public enum YtDlpEventKind
{
    Info,
    Progress,
    Completed,
    Destination,
    Error
}

public sealed record YtDlpEvent(
    YtDlpEventKind Kind,
    double Percent,
    long? TotalBytes,
    long? ReceivedBytes,
    long? SpeedBytesPerSecond,
    string? Eta,
    string? Error,
    string? DestinationPath);
