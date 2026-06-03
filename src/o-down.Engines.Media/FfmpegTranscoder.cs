using System.Diagnostics;
using o_down.Core.Models;
using Microsoft.Extensions.Logging;

namespace o_down.Engines.Media;

public sealed class FfmpegTranscoder
{
    private readonly string _ffmpegPath;
    private readonly ILogger<FfmpegTranscoder>? _logger;

    public FfmpegTranscoder(string ffmpegPath, ILogger<FfmpegTranscoder>? logger = null)
    {
        _ffmpegPath = ffmpegPath;
        _logger = logger;
    }

    public async Task<string> RemuxAsync(string input, string outputContainer, CancellationToken ct = default)
    {
        var outPath = Path.ChangeExtension(input, "." + outputContainer);
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = $"-y -i \"{input}\" -c copy \"{outPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        _logger?.LogInformation("ffmpeg remux: {Args}", psi.Arguments);
        using var p = Process.Start(psi)!;
        await p.WaitForExitAsync(ct).ConfigureAwait(false);
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg remux failed (exit {p.ExitCode})");
        return outPath;
    }

    public async Task<string> ExtractAudioAsync(string input, string codec, CancellationToken ct = default)
    {
        var outPath = Path.ChangeExtension(input, "." + codec);
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = $"-y -i \"{input}\" -vn -c:a {codec} -q:a 2 \"{outPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        _logger?.LogInformation("ffmpeg extract-audio: {Args}", psi.Arguments);
        using var p = Process.Start(psi)!;
        await p.WaitForExitAsync(ct).ConfigureAwait(false);
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg extract failed (exit {p.ExitCode})");
        return outPath;
    }
}
