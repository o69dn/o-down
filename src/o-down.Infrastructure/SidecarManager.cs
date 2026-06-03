using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using o_down.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace o_down.Infrastructure;

public sealed class SidecarManager : ISidecarManager
{
    private readonly ILogger<SidecarManager>? _logger;

    public SidecarManager(ILogger<SidecarManager>? logger = null)
    {
        _logger = logger;
        AppDirectory = AppContext.BaseDirectory;
        ToolsDirectory = Path.Combine(AppDirectory, "Tools");
        DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "o-down");
        LogDirectory = Path.Combine(DataDirectory, "logs");
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    public string AppDirectory { get; }
    public string ToolsDirectory { get; }
    public string DataDirectory { get; }
    public string LogDirectory { get; }

    public string Aria2Executable => ResolveBinary("aria2c", "aria2c.exe");
    public string YtDlpExecutable => ResolveBinary("yt-dlp", "yt-dlp.exe");
    public string FfmpegExecutable => ResolveBinary("ffmpeg", "ffmpeg.exe");

    private string ResolveBinary(string subdir, string exeName)
    {
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => "x64"
        };
        var local = Path.Combine(ToolsDirectory, subdir, arch, exeName);
        if (File.Exists(local)) return local;
        var flat = Path.Combine(ToolsDirectory, subdir, exeName);
        if (File.Exists(flat)) return flat;
        var pathHit = Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => Path.Combine(p, exeName))
            .FirstOrDefault(File.Exists);
        return pathHit ?? local;
    }

    public bool AreAllPresent() =>
        File.Exists(Aria2Executable) && File.Exists(YtDlpExecutable) && File.Exists(FfmpegExecutable);

    public async Task EnsureExtractedAsync(CancellationToken ct = default)
    {
        // Bundled sidecars are extracted on first run if marked as "extract on demand".
        // For o-down, sidecars are simply copied to output by MSBuild so this is a no-op
        // unless a future embedded-resource strategy is used.
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task<string> GetExecutableVersionAsync(string path, string versionArg, CancellationToken ct = default)
    {
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = versionArg,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                }
            };
            p.Start();
            var output = await p.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            return output.Trim();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to get version for {Path}", path);
            return string.Empty;
        }
    }
}
