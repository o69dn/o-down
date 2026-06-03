using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace o_down.Engines.Aria2;

public sealed class Aria2HostProcess : IAsyncDisposable
{
    private readonly ILogger<Aria2HostProcess>? _logger;
    private readonly string _exePath;
    private readonly string _downloadDir;
    private readonly string _configDir;
    private readonly string _sessionFile;
    private readonly int _port;
    private readonly string _secret;
    private Process? _process;
    private Task? _supervisor;
    private readonly CancellationTokenSource _cts = new();

    public Aria2HostProcess(string exePath, string downloadDir, string configDir, int port, string secret, ILogger<Aria2HostProcess>? logger = null)
    {
        _exePath = exePath;
        _downloadDir = downloadDir;
        _configDir = configDir;
        _port = port;
        _secret = secret;
        _sessionFile = Path.Combine(configDir, "aria2.session");
        _logger = logger;
    }

    public int Port => _port;
    public string Secret => _secret;

    public string BuildArgs(IEnumerable<string> extraOptions)
    {
        var sb = new StringBuilder();
        sb.Append("--enable-rpc ");
        sb.Append($"--rpc-listen-port={_port} ");
        sb.Append("--rpc-listen-all=false ");
        sb.Append("--rpc-allow-origin-all=false ");
        sb.Append("--rpc-secure=false ");
        sb.Append("--rpc-max-request-size=64M ");
        sb.Append("--console-log-level=warn ");
        sb.Append("--log-level=info ");
        sb.Append($"--log=\"{Path.Combine(_configDir, "aria2.log")}\" ");
        sb.Append($"--input-file=\"{_sessionFile}\" ");
        sb.Append($"--save-session=\"{_sessionFile}\" ");
        sb.Append($"--save-session-interval=10 ");
        sb.Append($"--dir=\"{_downloadDir}\" ");
        var confPath = Path.Combine(_configDir, "aria2.conf");
        if (File.Exists(confPath))
            sb.Append($"--conf-path=\"{confPath}\" ");
        foreach (var o in extraOptions) sb.Append(o).Append(' ');
        return sb.ToString();
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_configDir);
        Directory.CreateDirectory(_downloadDir);
        if (!File.Exists(_sessionFile))
            await File.WriteAllTextAsync(_sessionFile, string.Empty, ct).ConfigureAwait(false);
        var args = BuildArgs(new[]
        {
            "--max-connection-per-server=16",
            "--min-split-size=1M",
            "--split=16",
            "--max-concurrent-downloads=10",
            "--continue=true",
            "--auto-file-renaming=true",
            "--check-integrity=true",
            "--disk-cache=64M",
            "--file-allocation=falloc"
        });
        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _exePath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            }
        };
        _process.OutputDataReceived += (_, e) => { if (e.Data is not null) _logger?.LogDebug("[aria2c] {Line}", e.Data); };
        _process.ErrorDataReceived  += (_, e) => { if (e.Data is not null) _logger?.LogWarning("[aria2c] {Line}", e.Data); };
        if (!_process.Start())
            throw new InvalidOperationException("Failed to start aria2c");
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        _supervisor = Task.Run(() => SuperviseAsync(_cts.Token));
        await WaitForPortAsync(_port, TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
    }

    private async Task WaitForPortAsync(int port, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            try
            {
                using var tcp = new TcpClient();
                var connect = tcp.ConnectAsync("127.0.0.1", port);
                await Task.WhenAny(connect, Task.Delay(500, ct)).ConfigureAwait(false);
                if (connect.IsCompletedSuccessfully) return;
            }
            catch { }
            await Task.Delay(150, ct).ConfigureAwait(false);
        }
        throw new TimeoutException("aria2c RPC port did not open in time");
    }

    private async Task SuperviseAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_process is null) return;
                if (_process.HasExited)
                {
                    _logger?.LogWarning("aria2c exited with code {Code}, restarting", _process.ExitCode);
                    await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                    await StartAsync(ct).ConfigureAwait(false);
                    return;
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "aria2c supervisor error");
            }
            await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        catch { }
        _process?.Dispose();
        _cts.Dispose();
    }
}
