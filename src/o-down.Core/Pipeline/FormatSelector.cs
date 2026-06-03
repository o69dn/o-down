using o_down.Core.Models;

namespace o_down.Core.Pipeline;

/// <summary>
/// Picks a concrete <see cref="MediaFormat"/> from a <see cref="MediaProbe"/>
/// according to a <see cref="MediaFormatPreference"/>. The selector is pure
/// and side-effect-free so it can be tested without yt-dlp installed.
/// </summary>
public static class FormatSelector
{
    public static MediaFormat? Select(IReadOnlyList<MediaFormat> formats, MediaFormatPreference preference, string? customFormatId = null)
    {
        if (formats is null || formats.Count == 0) return null;

        return preference switch
        {
            MediaFormatPreference.Custom => formats.FirstOrDefault(f => f.Id == customFormatId),
            MediaFormatPreference.Best => BestCombined(formats),
            MediaFormatPreference.Worst => WorstCombined(formats),
            MediaFormatPreference.BestVideoPlusBestAudio => BestCombined(formats),
            MediaFormatPreference.BestVideoOnly => BestVideoOnly(formats),
            MediaFormatPreference.BestAudioOnly => BestAudioOnly(formats),
            MediaFormatPreference.Smallest => formats.OrderBy(f => f.ApproximateSize ?? long.MaxValue).First(),
            MediaFormatPreference.Largest => formats.OrderByDescending(f => f.ApproximateSize ?? 0).First(),
            _ => BestCombined(formats)
        };
    }

    public static string FormatSelectorExpression(MediaFormatPreference preference, string? customFormatId = null) => preference switch
    {
        MediaFormatPreference.Custom => customFormatId ?? "best",
        MediaFormatPreference.Best => "best",
        MediaFormatPreference.Worst => "worst",
        MediaFormatPreference.BestVideoPlusBestAudio => "bestvideo+bestaudio/best",
        MediaFormatPreference.BestVideoOnly => "bestvideo",
        MediaFormatPreference.BestAudioOnly => "bestaudio/best",
        MediaFormatPreference.Smallest => "worst",
        MediaFormatPreference.Largest => "best",
        _ => "best"
    };

    private static MediaFormat? BestCombined(IReadOnlyList<MediaFormat> formats)
    {
        return formats
            .Where(f => f.HeightFromResolution().HasValue || HasAudio(f))
            .OrderByDescending(f => f.HeightFromResolution() ?? 0)
            .ThenByDescending(f => HasAudio(f))
            .FirstOrDefault()
            ?? formats.FirstOrDefault();
    }

    private static MediaFormat? WorstCombined(IReadOnlyList<MediaFormat> formats)
    {
        return formats
            .Where(f => f.HeightFromResolution().HasValue || HasAudio(f))
            .OrderBy(f => f.HeightFromResolution() ?? int.MaxValue)
            .FirstOrDefault()
            ?? formats.FirstOrDefault();
    }

    private static MediaFormat? BestVideoOnly(IReadOnlyList<MediaFormat> formats)
    {
        return formats
            .Where(f => f.HeightFromResolution().HasValue)
            .OrderByDescending(f => f.HeightFromResolution()!.Value)
            .FirstOrDefault();
    }

    private static MediaFormat? BestAudioOnly(IReadOnlyList<MediaFormat> formats)
    {
        return formats
            .Where(HasAudio)
            .OrderByDescending(f => f.ApproximateSize ?? 0)
            .FirstOrDefault();
    }

    private static bool HasAudio(MediaFormat f) => !string.IsNullOrEmpty(f.AudioCodec);

    private static int? HeightFromResolution(this MediaFormat f)
    {
        if (string.IsNullOrEmpty(f.Resolution)) return null;
        var s = f.Resolution.AsSpan();
        var trailing = s.IndexOf('p');
        if (trailing > 0) s = s[..trailing];
        return int.TryParse(s, out var h) ? h : null;
    }
}
