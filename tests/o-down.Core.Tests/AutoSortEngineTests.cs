using o_down.Core.Models;
using o_down.Core.Pipeline;
using Xunit;

namespace o_down.Core.Tests;

public class AutoSortEngineTests
{
    [Fact]
    public void Match_ReturnsRuleByExtension()
    {
        var rules = new List<CategoryRule>
        {
            new() { Name = "Video", ExtensionPattern = "*.mp4;*.mkv", DestinationDirectory = "D:\\Videos" },
            new() { Name = "Docs",  ExtensionPattern = "*.pdf",         DestinationDirectory = "D:\\Docs"  }
        };
        var engine = new AutoSortEngine(rules);
        var item = new DownloadItem { FilenameHint = "movie.mp4" };
        var match = engine.Match(item);
        Assert.NotNull(match);
        Assert.Equal("Video", match!.Name);
    }

    [Fact]
    public void Match_ReturnsNullForUnknown()
    {
        var engine = new AutoSortEngine(new[]
        {
            new CategoryRule { Name = "Video", ExtensionPattern = "*.mp4" }
        });
        Assert.Null(engine.Match(new DownloadItem { FilenameHint = "unknown.xyz" }));
    }

    [Fact]
    public void Match_HonoursPriority()
    {
        var rules = new List<CategoryRule>
        {
            new() { Name = "General", ExtensionPattern = "*.bin", DestinationDirectory = "D:\\Gen", Priority = 1 },
            new() { Name = "Specific", NameRegex = "ubuntu.*\\.bin", DestinationDirectory = "D:\\Ubuntu", Priority = 10 }
        };
        var engine = new AutoSortEngine(rules);
        var match = engine.Match(new DownloadItem { FilenameHint = "ubuntu-22.04.bin" });
        Assert.Equal("Specific", match!.Name);
    }

    [Fact]
    public void Match_RespectsDisabledRule()
    {
        var engine = new AutoSortEngine(new[]
        {
            new CategoryRule { Name = "Disabled", ExtensionPattern = "*.mp4", IsEnabled = false }
        });
        Assert.Null(engine.Match(new DownloadItem { FilenameHint = "x.mp4" }));
    }
}
