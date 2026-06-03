namespace o_down.Core.Models;

public sealed class MagnetLinkInfo
{
    public string InfoHash { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public long? Size { get; set; }
    public List<string> Trackers { get; set; } = new();
    public List<string> WebSeeds { get; set; } = new();
    public List<string> ExactSources { get; set; } = new();
    public string? KeywordTopic { get; set; }
    public string? AcceptableSource { get; set; }
}
