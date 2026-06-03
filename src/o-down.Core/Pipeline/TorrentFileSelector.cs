using System.Text.RegularExpressions;
using o_down.Core.Abstractions;

namespace o_down.Core.Pipeline;

public static class TorrentFileSelector
{
    private static readonly string[] VideoExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".webm", ".m4v", ".flv", ".wmv", ".ts" };
    private static readonly string[] AudioExtensions = { ".mp3", ".flac", ".aac", ".ogg", ".opus", ".m4a", ".wav", ".wma" };
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tiff" };
    private static readonly string[] SubtitleExtensions = { ".srt", ".ass", ".ssa", ".vtt", ".sub" };

    public static List<string> Select(IReadOnlyList<TorrentFile> files, string spec)
    {
        if (string.IsNullOrWhiteSpace(spec) || spec.Equals("all", StringComparison.OrdinalIgnoreCase))
            return files.Select(f => f.Path).ToList();

        var trimmed = spec.Trim();
        if (trimmed.Equals("video", StringComparison.OrdinalIgnoreCase))
            return files.Where(f => HasExt(f.Path, VideoExtensions)).Select(f => f.Path).ToList();
        if (trimmed.Equals("audio", StringComparison.OrdinalIgnoreCase))
            return files.Where(f => HasExt(f.Path, AudioExtensions)).Select(f => f.Path).ToList();
        if (trimmed.Equals("images", StringComparison.OrdinalIgnoreCase))
            return files.Where(f => HasExt(f.Path, ImageExtensions)).Select(f => f.Path).ToList();
        if (trimmed.Equals("subs", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("subtitles", StringComparison.OrdinalIgnoreCase))
            return files.Where(f => HasExt(f.Path, SubtitleExtensions)).Select(f => f.Path).ToList();
        if (trimmed.StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
        {
            var rx = new Regex(trimmed[6..], RegexOptions.IgnoreCase);
            return files.Where(f => rx.IsMatch(f.Path)).Select(f => f.Path).ToList();
        }
        if (trimmed.StartsWith("size>", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseSize(trimmed[5..], out var min))
                return files.Where(f => f.Size > min).Select(f => f.Path).ToList();
        }
        if (trimmed.StartsWith("size<", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseSize(trimmed[5..], out var max))
                return files.Where(f => f.Size < max).Select(f => f.Path).ToList();
        }
        if (trimmed.StartsWith("ext:", StringComparison.OrdinalIgnoreCase))
        {
            var exts = trimmed[4..].Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.StartsWith('.') ? e : "." + e)
                .ToArray();
            return files.Where(f => HasExt(f.Path, exts)).Select(f => f.Path).ToList();
        }
        if (trimmed.Contains(','))
        {
            var indices = trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), out var i) ? i : -1)
                .Where(i => i >= 0 && i < files.Count)
                .ToList();
            if (indices.Count > 0)
                return indices.Select(i => files[i].Path).ToList();
        }
        if (int.TryParse(trimmed, out var single) && single >= 0 && single < files.Count)
            return new List<string> { files[single].Path };
        return new List<string>();
    }

    private static bool HasExt(string path, string[] exts)
    {
        var ext = System.IO.Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && exts.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryParseSize(string text, out long bytes)
    {
        bytes = 0;
        text = text.Trim();
        long unit = 1;
        if (text.EndsWith("GB", StringComparison.OrdinalIgnoreCase)) { unit = 1024L * 1024 * 1024; text = text.Substring(0, text.Length - 2); }
        else if (text.EndsWith("MB", StringComparison.OrdinalIgnoreCase)) { unit = 1024L * 1024; text = text.Substring(0, text.Length - 2); }
        else if (text.EndsWith("KB", StringComparison.OrdinalIgnoreCase)) { unit = 1024L; text = text.Substring(0, text.Length - 2); }
        else if (text.EndsWith("B", StringComparison.OrdinalIgnoreCase)) { text = text.Substring(0, text.Length - 1); }
        return double.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var n) && ((bytes = (long)(n * unit)) >= 0);
    }
}
