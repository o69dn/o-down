using o_down.Core.Pipeline;
using Xunit;

namespace o_down.Core.Tests;

public class YtDlpProgressParserTests
{
    [Fact]
    public void Parse_Empty_ReturnsNull()
    {
        Assert.Null(YtDlpProgressParser.Parse(""));
        Assert.Null(YtDlpProgressParser.Parse("   "));
    }

    [Fact]
    public void Parse_ProgressLine_PercentTotalSpeedEta()
    {
        var line = "[download]  42.0% of   10.34MiB at    1.23MiB/s ETA 00:04 (frag 5/10)";
        var ev = YtDlpProgressParser.Parse(line);
        Assert.NotNull(ev);
        Assert.Equal(YtDlpEventKind.Progress, ev!.Kind);
        Assert.Equal(42.0, ev.Percent);
        Assert.NotNull(ev.TotalBytes);
        Assert.True(ev.TotalBytes! > 10_000_000);
        Assert.NotNull(ev.SpeedBytesPerSecond);
        Assert.True(ev.SpeedBytesPerSecond! > 1_000_000);
        Assert.Equal("00:04", ev.Eta);
    }

    [Fact]
    public void Parse_ProgressLine_NoSpeedOrEta()
    {
        var ev = YtDlpProgressParser.Parse("[download]  10.0% of   5.00MiB");
        Assert.NotNull(ev);
        Assert.Equal(YtDlpEventKind.Progress, ev!.Kind);
        Assert.Null(ev.SpeedBytesPerSecond);
        Assert.Null(ev.Eta);
    }

    [Fact]
    public void Parse_CompleteLine_IsCompleted()
    {
        var ev = YtDlpProgressParser.Parse("[download] 100% of   10.34MiB in 00:00:08 at 1.23MiB/s");
        Assert.NotNull(ev);
        Assert.Equal(YtDlpEventKind.Completed, ev!.Kind);
        Assert.Equal(100.0, ev.Percent);
    }

    [Fact]
    public void Parse_DestinationLine_ExtractsPath()
    {
        var ev = YtDlpProgressParser.Parse("[download] Destination: Some Title [abc123].mp4");
        Assert.NotNull(ev);
        Assert.Equal(YtDlpEventKind.Destination, ev!.Kind);
        Assert.Equal("Some Title [abc123].mp4", ev.DestinationPath);
    }

    [Fact]
    public void Parse_MergerDestination_AlsoRecognized()
    {
        var ev = YtDlpProgressParser.Parse("[Merger] Merging formats into \"Some Title [abc123].mkv\"");
        Assert.NotNull(ev);
        Assert.Equal(YtDlpEventKind.Destination, ev!.Kind);
        Assert.Equal("Some Title [abc123].mkv", ev.DestinationPath);
    }

    [Fact]
    public void Parse_ErrorLine_ExtractsMessage()
    {
        var ev = YtDlpProgressParser.Parse("ERROR: unable to extract video data");
        Assert.NotNull(ev);
        Assert.Equal(YtDlpEventKind.Error, ev!.Kind);
        Assert.Equal("unable to extract video data", ev.Error);
    }

    [Fact]
    public void Parse_InfoLine_ReturnedAsInfo()
    {
        var ev = YtDlpProgressParser.Parse("[info] Available formats for abc123");
        Assert.NotNull(ev);
        Assert.Equal(YtDlpEventKind.Info, ev!.Kind);
    }

    [Fact]
    public void Parse_GarbageLine_ReturnedAsInfo()
    {
        var ev = YtDlpProgressParser.Parse("garbage with no recognizable pattern");
        Assert.NotNull(ev);
        Assert.Equal(YtDlpEventKind.Info, ev!.Kind);
    }

    [Theory]
    [InlineData("1024",          1024L)]
    [InlineData("10.34MiB",      (long)(10.34 * 1024 * 1024))]
    [InlineData("1.5GiB",        (long)(1.5 * 1024 * 1024 * 1024))]
    [InlineData("500KiB",        (long)(500 * 1024))]
    [InlineData("2.0TiB",        (long)(2.0 * 1024 * 1024 * 1024 * 1024))]
    [InlineData("100",           100L)]
    public void TryParseSize_ParsesUnits(string input, long expected)
    {
        var n = YtDlpProgressParser.TryParseSize(input);
        Assert.NotNull(n);
        Assert.Equal(expected, n!.Value);
    }

    [Fact]
    public void TryParseSize_NullOrEmptyReturnsNull()
    {
        Assert.Null(YtDlpProgressParser.TryParseSize(null));
        Assert.Null(YtDlpProgressParser.TryParseSize(""));
    }

    [Theory]
    [InlineData("1.23MiB/s",  (long)(1.23 * 1024 * 1024))]
    [InlineData("500KiB/s",   (long)(500 * 1024))]
    [InlineData("1024B/s",    1024L)]
    public void TryParseSpeed_ParsesRate(string input, long expected)
    {
        var n = YtDlpProgressParser.TryParseSpeed(input);
        Assert.NotNull(n);
        Assert.Equal(expected, n!.Value);
    }
}
