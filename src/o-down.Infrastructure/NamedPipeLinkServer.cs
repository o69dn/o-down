using System.IO.Pipes;
using Microsoft.Extensions.Logging;
using o_down.Core.Abstractions;
using o_down.Core.Protocol;

namespace o_down.Infrastructure;

public sealed class NamedPipeLinkServer : ILinkGraber, IAsyncDisposable
{
    public const string DefaultPipeName = "o-down-link";

    private readonly ILogger<NamedPipeLinkServer>? _logger;
    private readonly string _pipeName;
    private readonly int _maxConcurrentListeners;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<Task> _listeners = new();
    private Func<CapturedLink, CancellationToken, Task<NativeMessageCodec.NativeResponse>>? _responder;
    private int _started;

    public event EventHandler<CapturedLink>? LinkCaptured;

    public string PipeName => _pipeName;

    public NamedPipeLinkServer(
        ILogger<NamedPipeLinkServer>? logger = null,
        string pipeName = DefaultPipeName,
        int maxConcurrentListeners = 4)
    {
        _logger = logger;
        _pipeName = pipeName;
        _maxConcurrentListeners = maxConcurrentListeners;
    }

    public void SetResponder(Func<CapturedLink, CancellationToken, Task<NativeMessageCodec.NativeResponse>> responder)
    {
        _responder = responder ?? throw new ArgumentNullException(nameof(responder));
    }

    public Task PushAsync(CapturedLink link, CancellationToken ct = default)
    {
        LinkCaptured?.Invoke(this, link);
        return Task.CompletedTask;
    }

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        for (int i = 0; i < _maxConcurrentListeners; i++)
        {
            _listeners.Add(Task.Run(() => ListenLoop(_cts.Token)));
        }
        _logger?.LogInformation("NamedPipeLinkServer listening on \\\\.\\pipe\\{Pipe} ({N} listeners)", _pipeName, _maxConcurrentListeners);
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    _maxConcurrentListeners,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                _ = Task.Run(() => HandleAsync(server, ct), ct);
            }
            catch (OperationCanceledException)
            {
                server?.Dispose();
                return;
            }
            catch (Exception ex)
            {
                server?.Dispose();
                _logger?.LogWarning(ex, "Named pipe accept failed");
                try { await Task.Delay(200, ct).ConfigureAwait(false); } catch { return; }
            }
        }
    }

    private async Task HandleAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        try
        {
            var link = await NativeMessageCodec.ReadRequestAsync(server, ct).ConfigureAwait(false);
            NativeMessageCodec.NativeResponse response;
            if (link is null)
            {
                response = new NativeMessageCodec.NativeResponse { Ok = false, Error = "empty request" };
            }
            else
            {
                if (_responder is not null)
                {
                    try { response = await _responder(link, ct).ConfigureAwait(false); }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Responder failed for {Url}", link.Url);
                        response = new NativeMessageCodec.NativeResponse { Ok = false, Error = ex.Message };
                    }
                }
                else
                {
                    LinkCaptured?.Invoke(this, link);
                    response = new NativeMessageCodec.NativeResponse { Ok = true };
                }
            }

            var responseBytes = NativeMessageCodec.EncodeResponse(response);
            await server.WriteAsync(responseBytes, ct).ConfigureAwait(false);
            await server.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Named pipe handler error");
        }
        finally
        {
            try { server.Disconnect(); } catch { }
            server.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _started, 0) == 0) return;
        try { _cts.Cancel(); } catch { }
        try { await Task.WhenAll(_listeners).ConfigureAwait(false); } catch { }
        _cts.Dispose();
    }
}
