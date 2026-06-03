namespace o_down.Core.Models;

public sealed class MediaFormat
{
    public string Id { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string? Resolution { get; set; }
    public string? VideoCodec { get; set; }
    public string? AudioCodec { get; set; }
    public long? ApproximateSize { get; set; }
    public string? Notes { get; set; }
    public string? Container { get; set; }
}

public sealed class MediaProbe
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Uploader { get; set; }
    public TimeSpan? Duration { get; set; }
    public string? Thumbnail { get; set; }
    public IReadOnlyList<MediaFormat> Formats { get; set; } = Array.Empty<MediaFormat>();
    public string? Site { get; set; }
}
