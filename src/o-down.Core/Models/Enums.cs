namespace o_down.Core.Models;

public enum DownloadKind
{
    Http = 0,
    Torrent = 1,
    Media = 2
}

public enum DownloadState
{
    Queued = 0,
    FetchingMetadata = 1,
    Running = 2,
    Paused = 3,
    WaitingForSchedule = 4,
    Verifying = 5,
    PostProcessing = 6,
    Completed = 7,
    Failed = 8,
    Removed = 9
}
