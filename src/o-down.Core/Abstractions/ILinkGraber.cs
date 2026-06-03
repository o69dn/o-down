namespace o_down.Core.Abstractions;

public interface ILinkGraber
{
    event EventHandler<CapturedLink>? LinkCaptured;
    Task PushAsync(CapturedLink link, CancellationToken ct = default);
}

public sealed class CapturedLink
{
    public string Url { get; set; } = string.Empty;
    public string? Referrer { get; set; }
    public string? Cookies { get; set; }
    public string? FilenameHint { get; set; }
    public string Source { get; set; } = "unknown";
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
}
