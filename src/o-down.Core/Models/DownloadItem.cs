namespace o_down.Core.Models;

public sealed class DownloadItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DownloadKind Kind { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    public string? ReferrerUrl { get; set; }
    public string? Cookies { get; set; }
    public string FilenameHint { get; set; } = string.Empty;
    public string DestinationDirectory { get; set; } = string.Empty;
    public string? FinalPath { get; set; }
    public DownloadState State { get; set; } = DownloadState.Queued;
    public long TotalBytes { get; set; } = -1;
    public long ReceivedBytes { get; set; }
    public int Priority { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
    public string? Checksum { get; set; }
    public string? ChecksumAlgorithm { get; set; }
    public string Category { get; set; } = "Default";

    public int? MaxConnections { get; set; }
    public int? MaxConnectionsPerServer { get; set; }
    public long? MinSplitSize { get; set; }
    public int? MaxTries { get; set; }
    public TimeSpan? RetryWait { get; set; }
    public TimeSpan? Timeout { get; set; }
    public TimeSpan? ConnectTimeout { get; set; }
    public long? MaxDownloadLimit { get; set; }
    public long? LowestSpeedLimit { get; set; }
    public string? Proxy { get; set; }
    public string? ProxyUser { get; set; }
    public string? ProxyPassword { get; set; }
    public string? UserAgent { get; set; }
    public string? CookiesHeader { get; set; }
    public List<Mirror> Mirrors { get; set; } = new();

    // Media (yt-dlp) options — ignored for HTTP/Torrent kinds
    public string? MediaFormatId { get; set; }
    public MediaFormatPreference MediaFormatPreference { get; set; } = MediaFormatPreference.BestVideoPlusBestAudio;
    public bool MediaAudioOnly { get; set; }
    public string? MediaAudioFormat { get; set; }
    public bool MediaWriteSubtitles { get; set; }
    public bool MediaEmbedSubtitles { get; set; }
    public string? MediaSubtitleLanguages { get; set; }
    public string? MediaOutputTemplate { get; set; }
    public string? MediaSponsorblockRemove { get; set; }
    public long? MediaChapterStart { get; set; }
    public long? MediaChapterEnd { get; set; }

    // Torrent (MonoTorrent) options — ignored for HTTP/Media kinds
    public bool TorrentSequential { get; set; }
    public bool TorrentFirstLastPieceFirst { get; set; } = true;
    public int? TorrentMaxConnections { get; set; }
    public long? TorrentMaxDownloadSpeed { get; set; }
    public List<string>? TorrentWantedFiles { get; set; }
    public int TorrentUploadSlots { get; set; } = 8;
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 5;
    public bool IsActive => State is DownloadState.Running
                                    or DownloadState.FetchingMetadata
                                    or DownloadState.Verifying
                                    or DownloadState.PostProcessing;
    public bool IsPaused => State == DownloadState.Paused;
    public bool IsFinished => State == DownloadState.Completed;
    public bool IsFailed => State == DownloadState.Failed;
    public double Progress
    {
        get
        {
            if (TotalBytes > 0) return (double)ReceivedBytes / TotalBytes;
            return State == DownloadState.Completed ? 1.0 : 0.0;
        }
    }
}
