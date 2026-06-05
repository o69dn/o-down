namespace o_down.Core.Models;

public sealed class BandwidthProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public long? MaxDownloadBytesPerSecond { get; set; }
    public long? MaxUploadBytesPerSecond { get; set; }
    public bool IsBuiltIn { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsUnlimited => MaxDownloadBytesPerSecond is null && MaxUploadBytesPerSecond is null;

    public string DisplaySpeed
    {
        get
        {
            if (IsUnlimited) return "Unlimited";
            var dl = FormatSpeed(MaxDownloadBytesPerSecond);
            var ul = FormatSpeed(MaxUploadBytesPerSecond);
            return dl == ul ? dl : $"↓{dl} ↑{ul}";
        }
    }

    private static string FormatSpeed(long? bytesPerSec)
    {
        if (bytesPerSec is null or <= 0) return "∞";
        var b = bytesPerSec.Value;
        if (b >= 1024L * 1024) return $"{b / (1024L * 1024)} MB/s";
        if (b >= 1024) return $"{b / 1024} KB/s";
        return $"{b} B/s";
    }
}

public static class BuiltInBandwidthProfiles
{
    public static readonly BandwidthProfile Unlimited = new()
    {
        Id = new Guid("00000000-0000-0000-0000-000000000001"),
        Name = "Unlimited",
        IsBuiltIn = true,
        SortOrder = 0
    };

    public static readonly BandwidthProfile Light1MB = new()
    {
        Id = new Guid("00000000-0000-0000-0000-000000000002"),
        Name = "1 MB/s (Cap)",
        MaxDownloadBytesPerSecond = 1024L * 1024,
        MaxUploadBytesPerSecond = 512L * 1024,
        IsBuiltIn = true,
        SortOrder = 1
    };

    public static readonly BandwidthProfile Medium5MB = new()
    {
        Id = new Guid("00000000-0000-0000-0000-000000000003"),
        Name = "5 MB/s (Cap)",
        MaxDownloadBytesPerSecond = 5L * 1024L * 1024,
        MaxUploadBytesPerSecond = 2L * 1024L * 1024,
        IsBuiltIn = true,
        SortOrder = 2
    };

    public static readonly BandwidthProfile Fast10MB = new()
    {
        Id = new Guid("00000000-0000-0000-0000-000000000004"),
        Name = "10 MB/s (Cap)",
        MaxDownloadBytesPerSecond = 10L * 1024L * 1024,
        MaxUploadBytesPerSecond = 5L * 1024L * 1024,
        IsBuiltIn = true,
        SortOrder = 3
    };

    public static IReadOnlyList<BandwidthProfile> All { get; } = new[] { Unlimited, Light1MB, Medium5MB, Fast10MB };
}
