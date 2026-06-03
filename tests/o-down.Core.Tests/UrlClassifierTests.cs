using o_down.Core.Models;
using o_down.Core.Pipeline;
using Xunit;

namespace o_down.Core.Tests;

public class UrlClassifierTests
{
    [Theory]
    [InlineData("https://example.com/file.zip",  DownloadKind.Http)]
    [InlineData("http://example.com/file.zip",   DownloadKind.Http)]
    [InlineData("magnet:?xt=urn:btih:abc",       DownloadKind.Torrent)]
    [InlineData("https://example.com/x.torrent", DownloadKind.Torrent)]
    [InlineData("https://youtube.com/watch?v=1", DownloadKind.Media)]
    [InlineData("https://youtu.be/abc",          DownloadKind.Media)]
    [InlineData("https://vimeo.com/123",         DownloadKind.Media)]
    [InlineData("https://soundcloud.com/track",  DownloadKind.Media)]
    [InlineData("https://reddit.com/r/abc",      DownloadKind.Media)]
    public void Classify_ReturnsCorrectKind(string url, DownloadKind expected)
    {
        Assert.Equal(expected, UrlClassifier.Classify(url));
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com",  true)]
    [InlineData("magnet:?xt=urn:btih", true)]
    [InlineData("not-a-url",           false)]
    [InlineData("",                    false)]
    [InlineData("   ",                 false)]
    public void IsUrl_DetectsUrlAndNonUrl(string input, bool expected)
    {
        Assert.Equal(expected, UrlClassifier.IsUrl(input));
    }
}
