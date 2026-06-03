using System.IO.Pipes;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace o_down.Infrastructure;

public sealed class SingleInstanceGuard : IDisposable
{
    private readonly string _mutexName;
    private readonly ILogger<SingleInstanceGuard>? _logger;
    private Mutex? _mutex;
    private bool _acquired;
    private CancellationTokenSource? _focusServerCts;
    private Task? _focusServerTask;

    public SingleInstanceGuard(string mutexName, ILogger<SingleInstanceGuard>? logger = null)
    {
        _mutexName = mutexName ?? throw new ArgumentNullException(nameof(mutexName));
        _logger = logger;
    }

    public bool IsFirstInstance => _acquired;
    public string MutexName => _mutexName;

    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: false, _mutexName, out var createdNew);
        if (!createdNew)
        {
            try { _mutex.Dispose(); } catch { /* best effort */ }
            _mutex = null;
            _acquired = false;
            return false;
        }
        try { _mutex.WaitOne(); }
        catch (AbandonedMutexException)
        {
            _logger?.LogWarning("Acquired abandoned mutex {Name}; continuing", _mutexName);
        }
        _acquired = true;
        return true;
    }

    public void StartFocusServer(string pipeName, Action<string> onFocusRequested, CancellationToken ct = default)
    {
        if (!_acquired) throw new InvalidOperationException("Focus server can only run on the first instance");
        _focusServerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _focusServerTask = Task.Run(() => RunFocusServerLoop(pipeName, onFocusRequested, _focusServerCts.Token), _focusServerCts.Token);
    }

    public static async Task<bool> SendFocusMessageAsync(string pipeName, string payload, TimeSpan timeout, CancellationToken ct = default)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            await client.ConnectAsync((int)timeout.TotalMilliseconds, ct).ConfigureAwait(false);
            var bytes = Encoding.UTF8.GetBytes(payload);
            await client.WriteAsync(bytes, ct).ConfigureAwait(false);
            await client.FlushAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException) { return false; }
        catch (IOException) { return false; }
    }

    private async Task RunFocusServerLoop(string pipeName, Action<string> onFocusRequested, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    pipeName, PipeDirection.In, maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var payload = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
                try { onFocusRequested?.Invoke(payload); }
                catch (Exception ex) { _logger?.LogWarning(ex, "Focus handler threw"); }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Focus server iteration failed; restarting");
                try { await Task.Delay(200, ct).ConfigureAwait(false); } catch { return; }
            }
        }
    }

    public void Dispose()
    {
        try { _focusServerCts?.Cancel(); } catch { /* best effort */ }
        try { _focusServerTask?.Wait(2000); } catch { /* best effort */ }
        _focusServerCts?.Dispose();
        if (_mutex is not null)
        {
            try { if (_acquired) _mutex.ReleaseMutex(); } catch { /* best effort */ }
            try { _mutex.Dispose(); } catch { /* best effort */ }
        }
    }
}
