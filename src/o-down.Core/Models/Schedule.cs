namespace o_down.Core.Models;

public sealed class Schedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string CronExpression { get; set; } = string.Empty;
    public bool StartDownloads { get; set; } = true;
    public bool PauseDownloads { get; set; }
    public bool ApplyBandwidthLimit { get; set; }
    public long? BandwidthLimitBytesPerSecond { get; set; }
    public bool ThrottleSystem { get; set; }
    public bool IsEnabled { get; set; } = true;
}
