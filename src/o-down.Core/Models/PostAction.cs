namespace o_down.Core.Models;

public sealed class PostAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DownloadId { get; set; }
    public PostActionKind Kind { get; set; }
    public string? Command { get; set; }
    public string? Arguments { get; set; }
    public string? WorkingDirectory { get; set; }
    public int Order { get; set; }
    public bool StopOnError { get; set; } = true;
}

public enum PostActionKind
{
    RunCommand = 0,
    Shutdown = 1,
    Hibernate = 2,
    Sleep = 3,
    Lock = 4,
    Logout = 5,
    OpenFolder = 6,
    OpenFile = 7
}
