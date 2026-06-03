using System.Diagnostics;
using System.IO;
using o_down.Core.Abstractions;
using o_down.Core.Protocol;
using o_down.Infrastructure;
using Xunit;

namespace o_down.Infrastructure.Tests;

[Trait("Category", "Integration")]
public class NativeMessagingHostIntegrationTests
{
    [Fact]
    public async Task HostExe_RoundTripsLinkThroughPipe()
    {
        var hostExe = LocateHostExe();
        Assert.True(File.Exists(hostExe), $"host EXE not found: {hostExe}");

        var pipeName = "odown-host-test-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeLinkServer(pipeName: pipeName, maxConcurrentListeners: 1);
        var got = new TaskCompletionSource<CapturedLink>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sent = Guid.NewGuid();
        server.SetResponder((link, _) =>
        {
            got.TrySetResult(link);
            return Task.FromResult(new NativeMessageCodec.NativeResponse { Ok = true, DownloadId = sent });
        });
        server.Start();
        await Task.Delay(50);

        var link = new CapturedLink
        {
            Url = "https://example.com/integration.exe",
            FilenameHint = "integration.exe",
            Source = "chrome-extension"
        };
        var req = NativeMessageCodec.EncodeRequest(link);
        var requestFile = Path.Combine(Path.GetTempPath(), "odown-req-" + Guid.NewGuid().ToString("N") + ".bin");
        var responseFile = Path.Combine(Path.GetTempPath(), "odown-resp-" + Guid.NewGuid().ToString("N") + ".bin");
        var logPath = Path.Combine(Path.GetTempPath(), "odown-host-" + Guid.NewGuid().ToString("N") + ".log");
        await File.WriteAllBytesAsync(requestFile, req);

        var cmdPsi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"\"{hostExe}\" < \"{requestFile}\" > \"{responseFile}\"\"",
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        cmdPsi.EnvironmentVariables["ODOWN_PIPE_NAME"] = pipeName;
        cmdPsi.EnvironmentVariables["ODOWN_HOST_LOG"] = logPath;

        using var cmd = Process.Start(cmdPsi)!;
        var stderrTask = cmd.StandardError.ReadToEndAsync();
        await cmd.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));

        var captured = await got.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var respBytes = await File.ReadAllBytesAsync(responseFile);
        var stderr = await stderrTask;

        try { File.Delete(requestFile); } catch { }
        try { File.Delete(responseFile); } catch { }
        try { File.Delete(logPath); } catch { }

        Assert.True(respBytes.Length > 0, $"response file empty; stderr: {stderr}");
        using var respMs = new MemoryStream(respBytes);
        var resp = await NativeMessageCodec.ReadResponseAsync(respMs);

        Assert.Equal(link.Url, captured.Url);
        Assert.Equal(link.FilenameHint, captured.FilenameHint);
        Assert.NotNull(resp);
        Assert.True(resp!.Ok);
        Assert.Equal(sent, resp.DownloadId);
    }

    private static string LocateHostExe()
    {
        var asm = typeof(NativeMessagingRegistrar).Assembly.Location;
        var dir = Path.GetDirectoryName(asm)!;
        var guesses = new[]
        {
            Path.Combine(dir, "o-down.NativeMessaging.exe"),
            Path.Combine(dir, "..", "..", "..", "..", "..", "src", "o-down.NativeMessaging", "bin", "Debug", "net8.0-windows10.0.19041.0", "o-down.NativeMessaging.exe")
        };
        return guesses.FirstOrDefault(File.Exists) ?? guesses[0];
    }
}
