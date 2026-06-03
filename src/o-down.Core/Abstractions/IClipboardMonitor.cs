namespace o_down.Core.Abstractions;

public interface IClipboardMonitor
{
    bool IsRunning { get; }
    event EventHandler<string>? TextCaptured;
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
    void Disable();
    void Enable();
}
