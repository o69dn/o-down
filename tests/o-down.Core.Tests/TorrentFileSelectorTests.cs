using o_down.Core.Abstractions;
using o_down.Core.Pipeline;
using Xunit;

namespace o_down.Core.Tests;

public class TorrentFileSelectorTests
{
    private static List<TorrentFile> Sample() => new()
    {
        new TorrentFile { Path = "movie.mp4", Size = 700_000_000 },
        new TorrentFile { Path = "sample/sample.mkv", Size = 1_500_000_000 },
        new TorrentFile { Path = "audio/track1.mp3", Size = 8_000_000 },
        new TorrentFile { Path = "audio/track2.flac", Size = 30_000_000 },
        new TorrentFile { Path = "images/poster.jpg", Size = 200_000 },
        new TorrentFile { Path = "subs/english.srt", Size = 50_000 },
        new TorrentFile { Path = "readme.txt", Size = 1_000 },
    };

    [Fact]
    public void Select_All_ReturnsEverything()
    {
        var result = TorrentFileSelector.Select(Sample(), "all");
        Assert.Equal(7, result.Count);
    }

    [Fact]
    public void Select_Video_ReturnsOnlyVideoFiles()
    {
        var result = TorrentFileSelector.Select(Sample(), "video");
        Assert.Contains("movie.mp4", result);
        Assert.Contains("sample/sample.mkv", result);
        Assert.DoesNotContain("audio/track1.mp3", result);
        Assert.DoesNotContain("readme.txt", result);
    }

    [Fact]
    public void Select_Audio_ReturnsOnlyAudioFiles()
    {
        var result = TorrentFileSelector.Select(Sample(), "audio");
        Assert.Equal(2, result.Count);
        var audioExts = new[] { ".mp3", ".flac", ".aac", ".ogg", ".opus", ".m4a", ".wav", ".wma" };
        Assert.All(result, p => Assert.Contains(System.IO.Path.GetExtension(p), audioExts));
    }

    [Fact]
    public void Select_Images_ReturnsOnlyImageFiles()
    {
        var result = TorrentFileSelector.Select(Sample(), "images");
        Assert.Single(result);
        Assert.Equal("images/poster.jpg", result[0]);
    }

    [Fact]
    public void Select_Subs_ReturnsOnlySubtitleFiles()
    {
        var result = TorrentFileSelector.Select(Sample(), "subs");
        Assert.Single(result);
        Assert.Equal("subs/english.srt", result[0]);
    }

    [Fact]
    public void Select_Regex_MatchesPaths()
    {
        var result = TorrentFileSelector.Select(Sample(), "regex:^audio/");
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Select_ExtensionList_FiltersByExt()
    {
        var result = TorrentFileSelector.Select(Sample(), "ext:jpg,srt");
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Select_Indices_ReturnsSpecificFiles()
    {
        var files = Sample();
        var result = TorrentFileSelector.Select(files, "0,2,4");
        Assert.Equal(new[] { "movie.mp4", "audio/track1.mp3", "images/poster.jpg" }, result);
    }

    [Fact]
    public void Select_SingleIndex_ReturnsThatFile()
    {
        var result = TorrentFileSelector.Select(Sample(), "1");
        Assert.Single(result);
        Assert.Equal("sample/sample.mkv", result[0]);
    }

    [Fact]
    public void Select_SizeGreater_ReturnsLargeFiles()
    {
        var result = TorrentFileSelector.Select(Sample(), "size>500MB");
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Select_SizeLess_ReturnsSmallFiles()
    {
        var result = TorrentFileSelector.Select(Sample(), "size<1MB");
        Assert.Equal(3, result.Count);
        Assert.Contains("images/poster.jpg", result);
        Assert.Contains("subs/english.srt", result);
        Assert.Contains("readme.txt", result);
    }

    [Fact]
    public void Select_EmptySpec_ReturnsAll()
    {
        var result = TorrentFileSelector.Select(Sample(), "");
        Assert.Equal(7, result.Count);
    }

    [Fact]
    public void Select_NoMatch_ReturnsEmpty()
    {
        var result = TorrentFileSelector.Select(Sample(), "regex:^nothing$");
        Assert.Empty(result);
    }
}
