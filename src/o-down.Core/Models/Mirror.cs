namespace o_down.Core.Models;

public sealed class Mirror
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DownloadId { get; set; }
    public string Url { get; set; } = string.Empty;
    public int Priority { get; set; }
    public bool IsHealthy { get; set; } = true;
    public DateTimeOffset LastProbedAt { get; set; } = DateTimeOffset.UtcNow;
}
