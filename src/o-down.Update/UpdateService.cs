using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace o_down.Update;

public sealed class UpdateManifest
{
    [JsonPropertyName("version")]      public string Version { get; set; } = string.Empty;
    [JsonPropertyName("releaseDate")]  public DateTimeOffset ReleaseDate { get; set; }
    [JsonPropertyName("downloadUrl")]  public string DownloadUrl { get; set; } = string.Empty;
    [JsonPropertyName("sha256")]       public string Sha256 { get; set; } = string.Empty;
    [JsonPropertyName("sizeBytes")]    public long SizeBytes { get; set; }
    [JsonPropertyName("channel")]      public string Channel { get; set; } = "stable";
    [JsonPropertyName("notes")]        public string? Notes { get; set; }
    [JsonPropertyName("minWindows")]   public string? MinWindows { get; set; }
    [JsonPropertyName("exePath")]      public string ExePath { get; set; } = "o-down.App.exe";
}

public sealed class UpdateCheckResult
{
    public UpdateManifest? Manifest { get; set; }
    public string? Error { get; set; }
    public bool HasUpdate { get; set; }
    public string CurrentVersion { get; set; } = string.Empty;
}

public interface IUpdateFeed
{
    Task<UpdateManifest?> GetLatestAsync(string channel, CancellationToken ct = default);
}

public sealed class HttpUpdateFeed : IUpdateFeed
{
    private readonly HttpClient _http;
    public HttpUpdateFeed(HttpClient http) => _http = http;

    public async Task<UpdateManifest?> GetLatestAsync(string channel, CancellationToken ct = default)
    {
        var url = $"https://updates.example.com/o-down/{channel}/latest.json";
        try
        {
            return await _http.GetFromJsonAsync<UpdateManifest>(url, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }
}

public sealed class UpdateService
{
    private readonly IUpdateFeed _feed;
    private readonly string _appDir;
    private readonly HttpClient _http;
    private readonly Version _current;
    private readonly ILogger<UpdateService>? _logger;

    public UpdateService(IUpdateFeed feed, string appDir, Version current, HttpClient? http = null, ILogger<UpdateService>? logger = null)
    {
        _feed = feed;
        _appDir = appDir;
        _current = current;
        _http = http ?? new HttpClient();
        _logger = logger;
    }

    public string CurrentVersion => _current.ToString();
    public string AppDirectory => _appDir;

    public static bool IsNewer(string? remote, string? current)
    {
        if (!Version.TryParse(remote, out var r)) return false;
        if (!Version.TryParse(current, out var c)) return true;
        return r > c;
    }

    public async Task<UpdateCheckResult> CheckAsync(string channel, CancellationToken ct = default)
    {
        var result = new UpdateCheckResult { CurrentVersion = _current.ToString() };
        var manifest = await _feed.GetLatestAsync(channel, ct).ConfigureAwait(false);
        if (manifest is null) { result.Error = "No manifest"; return result; }
        result.Manifest = manifest;
        result.HasUpdate = IsNewer(manifest.Version, _current.ToString());
        return result;
    }

    public async Task<string> DownloadAsync(UpdateManifest manifest, string destDir, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(destDir);
        var localPath = Path.Combine(destDir, $"o-down-{manifest.Version}.zip");
        using var resp = await _http.GetAsync(manifest.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        if (manifest.SizeBytes > 0 && resp.Content.Headers.ContentLength is long len && len != manifest.SizeBytes)
            throw new InvalidOperationException($"Size mismatch: header says {len} bytes, manifest says {manifest.SizeBytes}");
        await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var dst = File.Create(localPath);
        var buffer = new byte[64 * 1024];
        long total = resp.Content.Headers.ContentLength ?? -1;
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            read += n;
            if (total > 0) progress?.Report((double)read / total);
        }
        if (total > 0 && read != total)
            throw new InvalidOperationException($"Truncated download: got {read}, expected {total}");
        return localPath;
    }

    public async Task<bool> VerifySha256Async(string zipPath, string expectedHex, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(expectedHex)) return true;
        await using var src = File.OpenRead(zipPath);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(src, ct).ConfigureAwait(false);
        var actual = Convert.ToHexString(hash);
        var expected = expectedHex.Replace(" ", "").Replace("-", "").ToLowerInvariant();
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> StageAsync(string zipPath, string stagingDir, CancellationToken ct = default)
    {
        if (Directory.Exists(stagingDir))
            Directory.Delete(stagingDir, recursive: true);
        Directory.CreateDirectory(stagingDir);
        await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, stagingDir), ct).ConfigureAwait(false);
        return stagingDir;
    }

    public async Task<string> ApplyAsync(string zipPath, string currentExePath, CancellationToken ct = default)
    {
        if (!File.Exists(currentExePath))
            throw new FileNotFoundException("Current executable not found", currentExePath);
        var appDir = Path.GetDirectoryName(Path.GetFullPath(currentExePath))
            ?? throw new InvalidOperationException("Could not resolve app dir");
        var staging = Path.Combine(Path.GetDirectoryName(appDir) ?? Path.GetTempPath(),
            Path.GetFileName(appDir) + $".update-staging-{Guid.NewGuid():N}");
        var backup = Path.Combine(Path.GetDirectoryName(appDir) ?? Path.GetTempPath(),
            Path.GetFileName(appDir) + $".old-{DateTime.UtcNow:yyyyMMddHHmmss}");

        try
        {
            await StageAsync(zipPath, staging, ct).ConfigureAwait(false);

            if (Directory.Exists(backup))
                Directory.Delete(backup, recursive: true);
            Directory.Move(appDir, backup);

            try
            {
                Directory.Move(staging, appDir);
            }
            catch
            {
                if (Directory.Exists(appDir)) Directory.Delete(appDir, recursive: true);
                Directory.Move(backup, appDir);
                throw;
            }

            try { Directory.Delete(backup, recursive: true); } catch { /* best effort */ }
            return Path.Combine(appDir, Path.GetFileName(currentExePath));
        }
        catch
        {
            if (Directory.Exists(staging))
            {
                try { Directory.Delete(staging, recursive: true); } catch { /* best effort */ }
            }
            throw;
        }
    }
}
