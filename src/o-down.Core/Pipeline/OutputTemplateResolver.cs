using System.Text;
using System.Text.RegularExpressions;

namespace o_down.Core.Pipeline;

/// <summary>
/// Resolves a yt-dlp output template (e.g. <c>%(title)s [%(id)s].%(ext)s</c>)
/// against a <see cref="MediaProbe"/> to a concrete file path before launch.
/// yt-dlp itself does the same substitution at runtime, but we need the path
/// up-front so we can:
///   * reserve disk space,
///   * check for collisions,
///   * register the path with the engine as the "expected final path",
///   * produce a deterministic name the user can read in the queue.
/// </summary>
public static class OutputTemplateResolver
{
    private static readonly Regex TokenRegex = new(@"%(\((?<flag>[^)]+)\))?(?<name>[a-zA-Z_]*)s", RegexOptions.Compiled);

    public static string Resolve(string template, MediaTemplateContext ctx)
    {
        if (string.IsNullOrEmpty(template)) return SanitizeFileName(ctx.FallbackTitle) + ".%(ext)s";

        var sb = new StringBuilder(template.Length);
        int last = 0;
        foreach (Match m in TokenRegex.Matches(template))
        {
            sb.Append(template, last, m.Index - last);
            var key = m.Groups["flag"].Success && m.Groups["flag"].Value.Length > 0
                ? m.Groups["flag"].Value
                : m.Groups["name"].Value;
            sb.Append(ResolveToken(key, ctx));
            last = m.Index + m.Length;
        }
        sb.Append(template, last, template.Length - last);
        return sb.ToString();
    }

    public static string SanitizeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "untitled";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }
        var s = sb.ToString().Trim('.', ' ');
        return string.IsNullOrEmpty(s) ? "untitled" : s;
    }

    private static string ResolveToken(string name, MediaTemplateContext ctx) => name switch
    {
        "title" => SanitizeFileName(ctx.Title),
        "id" => SanitizeFileName(ctx.Id),
        "ext" => ctx.Extension ?? "bin",
        "uploader" => SanitizeFileName(ctx.Uploader),
        "channel" => SanitizeFileName(ctx.Uploader),
        "playlist" => SanitizeFileName(ctx.Playlist),
        "playlist_index" => ctx.PlaylistIndex?.ToString() ?? "",
        "upload_date" => ctx.UploadDate ?? "",
        "duration" => ctx.DurationSeconds?.ToString() ?? "",
        "resolution" => ctx.Resolution ?? "",
        "fps" => ctx.Fps?.ToString() ?? "",
        "vcodec" => ctx.VideoCodec ?? "",
        "acodec" => ctx.AudioCodec ?? "",
        "epoch" => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
        _ => ""
    };
}

public sealed class MediaTemplateContext
{
    public string Title { get; set; } = "untitled";
    public string Id { get; set; } = string.Empty;
    public string? Extension { get; set; }
    public string? Uploader { get; set; }
    public string? Playlist { get; set; }
    public int? PlaylistIndex { get; set; }
    public string? UploadDate { get; set; }
    public double? DurationSeconds { get; set; }
    public string? Resolution { get; set; }
    public double? Fps { get; set; }
    public string? VideoCodec { get; set; }
    public string? AudioCodec { get; set; }
    public string FallbackTitle { get; set; } = "untitled";
}
