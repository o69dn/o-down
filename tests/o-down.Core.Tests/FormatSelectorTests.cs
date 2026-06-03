using o_down.Core.Models;
using o_down.Core.Pipeline;
using Xunit;

namespace o_down.Core.Tests;

public class FormatSelectorTests
{
    private static MediaFormat F(string id, string ext = "mp4", int? h = null, string? vcodec = null, string? acodec = null, long? size = null) => new()
    {
        Id = id,
        Extension = ext,
        Resolution = h.HasValue ? $"{h}p" : null,
        VideoCodec = vcodec,
        AudioCodec = acodec,
        ApproximateSize = size
    };

    [Fact]
    public void Select_EmptyList_ReturnsNull()
    {
        Assert.Null(FormatSelector.Select(Array.Empty<MediaFormat>(), MediaFormatPreference.Best));
    }

    [Fact]
    public void Select_Custom_UsesProvidedId()
    {
        var list = new[] { F("22", h: 720), F("18", h: 360) };
        var pick = FormatSelector.Select(list, MediaFormatPreference.Custom, "18");
        Assert.NotNull(pick);
        Assert.Equal("18", pick!.Id);
    }

    [Fact]
    public void Select_Custom_UnknownId_ReturnsNull()
    {
        var list = new[] { F("22") };
        Assert.Null(FormatSelector.Select(list, MediaFormatPreference.Custom, "missing"));
    }

    [Fact]
    public void Select_Best_PrefersHighestResolution()
    {
        var list = new[] { F("a", h: 480), F("b", h: 1080), F("c", h: 720) };
        var pick = FormatSelector.Select(list, MediaFormatPreference.Best);
        Assert.Equal("b", pick!.Id);
    }

    [Fact]
    public void Select_Worst_PrefersLowestResolution()
    {
        var list = new[] { F("a", h: 480), F("b", h: 1080), F("c", h: 720) };
        var pick = FormatSelector.Select(list, MediaFormatPreference.Worst);
        Assert.Equal("a", pick!.Id);
    }

    [Fact]
    public void Select_BestVideoOnly_ExcludesAudioOnly()
    {
        var list = new[]
        {
            F("audio", ext: "m4a", acodec: "aac"),
            F("video-low", h: 360, vcodec: "h264"),
            F("video-high", h: 1080, vcodec: "h264")
        };
        var pick = FormatSelector.Select(list, MediaFormatPreference.BestVideoOnly);
        Assert.Equal("video-high", pick!.Id);
    }

    [Fact]
    public void Select_BestAudioOnly_ExcludesVideoOnly()
    {
        var list = new[]
        {
            F("video", h: 720, vcodec: "h264"),
            F("audio-small", ext: "m4a", acodec: "aac", size: 1_000_000),
            F("audio-large", ext: "m4a", acodec: "aac", size: 5_000_000)
        };
        var pick = FormatSelector.Select(list, MediaFormatPreference.BestAudioOnly);
        Assert.Equal("audio-large", pick!.Id);
    }

    [Fact]
    public void Select_Smallest_PrefersSmallestSize()
    {
        var list = new[] { F("a", size: 5_000), F("b", size: 1_000), F("c", size: 9_000) };
        var pick = FormatSelector.Select(list, MediaFormatPreference.Smallest);
        Assert.Equal("b", pick!.Id);
    }

    [Fact]
    public void Select_Largest_PrefersLargestSize()
    {
        var list = new[] { F("a", size: 5_000), F("b", size: 1_000), F("c", size: 9_000) };
        var pick = FormatSelector.Select(list, MediaFormatPreference.Largest);
        Assert.Equal("c", pick!.Id);
    }

    [Fact]
    public void Select_Smallest_NullsAreTreatedAsInfinity()
    {
        var list = new[] { F("a", size: 1_000), F("b", size: null), F("c", size: 5_000) };
        var pick = FormatSelector.Select(list, MediaFormatPreference.Smallest);
        Assert.Equal("a", pick!.Id);
    }

    [Fact]
    public void FormatSelectorExpression_GeneratesValidYtDlpSyntax()
    {
        Assert.Equal("best", FormatSelector.FormatSelectorExpression(MediaFormatPreference.Best));
        Assert.Equal("worst", FormatSelector.FormatSelectorExpression(MediaFormatPreference.Worst));
        Assert.Equal("bestvideo+bestaudio/best", FormatSelector.FormatSelectorExpression(MediaFormatPreference.BestVideoPlusBestAudio));
        Assert.Equal("bestaudio/best", FormatSelector.FormatSelectorExpression(MediaFormatPreference.BestAudioOnly));
    }

    [Fact]
    public void FormatSelectorExpression_CustomReturnsProvidedId()
    {
        Assert.Equal("299+140", FormatSelector.FormatSelectorExpression(MediaFormatPreference.Custom, "299+140"));
    }

    [Fact]
    public void Select_FallbackToFirstWhenNoResolutionOrAudio()
    {
        var list = new[] { F("only", h: null, acodec: null) };
        var pick = FormatSelector.Select(list, MediaFormatPreference.Best);
        Assert.NotNull(pick);
        Assert.Equal("only", pick!.Id);
    }
}
