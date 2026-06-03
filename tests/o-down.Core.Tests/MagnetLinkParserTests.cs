using o_down.Core.Pipeline;
using Xunit;

namespace o_down.Core.Tests;

public class MagnetLinkParserTests
{
    [Fact]
    public void Parse_BitTorrentHash_IsExtractedFromXt()
    {
        var info = MagnetLinkParser.Parse("magnet:?xt=urn:btih:dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c");
        Assert.Equal("dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c", info.InfoHash);
    }

    [Fact]
    public void Parse_DisplayName_IsExtractedFromDn()
    {
        var info = MagnetLinkParser.Parse("magnet:?xt=urn:btih:abc&dn=ubuntu-22.04.iso");
        Assert.Equal("ubuntu-22.04.iso", info.DisplayName);
    }

    [Fact]
    public void Parse_Trackers_AreCollected()
    {
        var info = MagnetLinkParser.Parse(
            "magnet:?xt=urn:btih:abc&tr=udp%3A%2F%2Ftracker1.example.com%3A1337&tr=udp%3A%2F%2Ftracker2.example.com%3A1337");
        Assert.Equal(2, info.Trackers.Count);
        Assert.Equal("udp://tracker1.example.com:1337", info.Trackers[0]);
        Assert.Equal("udp://tracker2.example.com:1337", info.Trackers[1]);
    }

    [Fact]
    public void Parse_AllKnownFields_ArePopulated()
    {
        var info = MagnetLinkParser.Parse(
            "magnet:?xt=urn:btih:abc&dn=My%20File&xl=1048576&tr=udp%3A%2F%2Ft.example.com%3A1337&ws=https%3A%2F%2Fseed.example.com%2Ffile&kt=foo&as=https%3A%2F%2Falt.example.com%2F");
        Assert.Equal("abc", info.InfoHash);
        Assert.Equal("My File", info.DisplayName);
        Assert.Equal(1048576, info.Size);
        Assert.Single(info.Trackers);
        Assert.Single(info.WebSeeds);
        Assert.Equal("foo", info.KeywordTopic);
        Assert.Equal("https://alt.example.com/", info.AcceptableSource);
    }

    [Fact]
    public void Parse_ExactSources_AreCollected()
    {
        var info = MagnetLinkParser.Parse(
            "magnet:?xt=urn:btih:abc&xs=https%3A%2F%2Fcache.example.com%2Ffile&xs=https%3A%2F%2Fcache2.example.com%2Ffile");
        Assert.Equal(2, info.ExactSources.Count);
        Assert.Equal("https://cache.example.com/file", info.ExactSources[0]);
    }

    [Fact]
    public void Parse_BitTorrentV2Hash_IsExtracted()
    {
        var info = MagnetLinkParser.Parse("magnet:?xt=urn:btmh:1220cafee9");
        Assert.Equal("1220cafee9", info.InfoHash);
    }

    [Fact]
    public void Parse_NotMagnet_Throws()
    {
        Assert.Throws<FormatException>(() => MagnetLinkParser.Parse("https://example.com/file.torrent"));
    }

    [Fact]
    public void Parse_EmptyString_Throws()
    {
        Assert.Throws<ArgumentException>(() => MagnetLinkParser.Parse(""));
    }
}
