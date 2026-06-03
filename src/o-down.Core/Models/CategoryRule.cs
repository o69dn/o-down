namespace o_down.Core.Models;

public sealed class CategoryRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string DestinationDirectory { get; set; } = string.Empty;
    public string? ExtensionPattern { get; set; }
    public string? NameRegex { get; set; }
    public int Priority { get; set; }
    public bool IsEnabled { get; set; } = true;
}
