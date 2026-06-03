using o_down.Core.Models;

namespace o_down.Core.Abstractions;

public interface ISidecarManager
{
    string AppDirectory { get; }
    string ToolsDirectory { get; }
    string DataDirectory { get; }
    string LogDirectory { get; }
    string Aria2Executable { get; }
    string YtDlpExecutable { get; }
    string FfmpegExecutable { get; }
    Task EnsureExtractedAsync(CancellationToken ct = default);
    bool AreAllPresent();
    Task<string> GetExecutableVersionAsync(string path, string versionArg, CancellationToken ct = default);
}
