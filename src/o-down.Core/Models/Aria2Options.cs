using System.Globalization;
using o_down.Core.Models;

namespace o_down.Core.Models;

public sealed class Aria2Options
{
    public string? Out { get; set; }
    public string? Dir { get; set; }
    public string? Header { get; set; }
    public string? UserAgent { get; set; }
    public string? Checksum { get; set; }
    public int? Split { get; set; }
    public int? MaxConnectionPerServer { get; set; }
    public long? MinSplitSize { get; set; }
    public int? MaxTries { get; set; }
    public TimeSpan? RetryWait { get; set; }
    public TimeSpan? Timeout { get; set; }
    public TimeSpan? ConnectTimeout { get; set; }
    public long? MaxDownloadLimit { get; set; }
    public long? LowestSpeedLimit { get; set; }
    public string? AllProxy { get; set; }
    public string? AllProxyUser { get; set; }
    public string? AllProxyPasswd { get; set; }
    public string? Referer { get; set; }
    public string? Cookies { get; set; }
    public bool? Continue { get; set; }
    public bool? AutoFileRenaming { get; set; }
    public bool? CheckIntegrity { get; set; }
    public string? FileAllocation { get; set; }

    public static Aria2Options FromDownloadItem(DownloadItem item)
    {
        var o = new Aria2Options
        {
            Dir = item.DestinationDirectory,
            Out = string.IsNullOrEmpty(item.FilenameHint) ? null : item.FilenameHint,
            UserAgent = item.UserAgent ?? "o-down/1.0",
            MaxConnectionPerServer = item.MaxConnectionsPerServer,
            Split = item.MaxConnections,
            MinSplitSize = item.MinSplitSize,
            MaxTries = item.MaxTries,
            RetryWait = item.RetryWait,
            Timeout = item.Timeout,
            ConnectTimeout = item.ConnectTimeout,
            MaxDownloadLimit = item.MaxDownloadLimit,
            LowestSpeedLimit = item.LowestSpeedLimit,
            AllProxy = item.Proxy,
            AllProxyUser = item.ProxyUser,
            AllProxyPasswd = item.ProxyPassword,
            Referer = item.ReferrerUrl,
            Cookies = item.CookiesHeader
        };
        if (!string.IsNullOrEmpty(item.Checksum) && !string.IsNullOrEmpty(item.ChecksumAlgorithm))
            o.Checksum = $"{item.ChecksumAlgorithm}={item.Checksum}";
        return o;
    }

    public IReadOnlyDictionary<string, object?> ToRpcOptions()
    {
        var d = new Dictionary<string, object?>();
        void Set(string k, object? v) { if (v is not null) d[k] = v; }

        Set("dir", Dir);
        Set("out", Out);
        Set("user-agent", UserAgent);
        Set("checksum", Checksum);
        Set("split", Split);
        Set("max-connection-per-server", MaxConnectionPerServer);
        Set("min-split-size", MinSplitSize);
        Set("max-tries", MaxTries);
        Set("retry-wait", RetryWait);
        Set("timeout", Timeout);
        Set("connect-timeout", ConnectTimeout);
        Set("max-download-limit", MaxDownloadLimit);
        Set("lowest-speed-limit", LowestSpeedLimit);
        Set("all-proxy", AllProxy);
        Set("all-proxy-user", AllProxyUser);
        Set("all-proxy-passwd", AllProxyPasswd);
        Set("referer", Referer);
        Set("cookie", Cookies);
        Set("continue", Continue);
        Set("auto-file-renaming", AutoFileRenaming);
        Set("check-integrity", CheckIntegrity);
        Set("file-allocation", FileAllocation);

        var headers = new List<string>();
        if (!string.IsNullOrEmpty(Header)) headers.Add(Header);
        if (!string.IsNullOrEmpty(Referer)) headers.Add($"Referer: {Referer}");
        if (!string.IsNullOrEmpty(Cookies)) headers.Add($"Cookie: {Cookies}");
        if (headers.Count > 0) d["header"] = headers.ToArray();

        return d;
    }

    public IReadOnlyDictionary<string, object?> ToRpcOptionsDelta(Aria2Options? previous)
    {
        var curr = ToRpcOptions();
        if (previous is null) return curr;
        var prev = previous.ToRpcOptions();
        var d = new Dictionary<string, object?>();
        foreach (var (k, v) in curr)
        {
            if (!prev.TryGetValue(k, out var old) || !Equals(old, v))
                d[k] = v;
        }
        return d;
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0";
        if (bytes % (1024L * 1024) == 0) return $"{bytes / (1024L * 1024)}M";
        if (bytes % 1024 == 0) return $"{bytes / 1024}K";
        return bytes.ToString(CultureInfo.InvariantCulture);
    }
}
