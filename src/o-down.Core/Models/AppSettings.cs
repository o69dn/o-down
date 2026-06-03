namespace o_down.Core.Models;

public sealed class AppSettings
{
    public string Version { get; set; } = "1";
    public string DefaultDownloadDirectory { get; set; } = string.Empty;
    public int MaxConcurrentDownloads { get; set; } = 5;
    public int MaxConnectionsPerDownload { get; set; } = 16;
    public long? GlobalBandwidthLimitBytesPerSecond { get; set; }
    public bool ClipboardMonitorEnabled { get; set; } = false;
    public bool BrowserExtensionEnabled { get; set; } = true;
    public string UpdateChannel { get; set; } = "stable";
    public bool AutoUpdateEnabled { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public string Theme { get; set; } = "System";
    public bool ConfirmBeforeRemove { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;

    public static AppSettings Default() => new();
}
