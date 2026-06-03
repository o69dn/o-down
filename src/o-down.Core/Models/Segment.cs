namespace o_down.Core.Models;

public sealed class Segment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DownloadId { get; set; }
    public int Index { get; set; }
    public long StartByte { get; set; }
    public long EndByte { get; set; }
    public long CompletedBytes { get; set; }
    public bool IsCompleted => CompletedBytes >= EndByte - StartByte + 1;
    public string? MirrorUrl { get; set; }
}
