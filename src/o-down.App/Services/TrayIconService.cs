using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace o_down.App.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly ILogger<TrayIconService>? _logger;
    public TrayIconService(ILogger<TrayIconService>? logger = null) => _logger = logger;

    public void Show()
    {
        // WinUI 3 has no built-in tray icon. Hook H.NotifyIcon.WinUI (NuGet) or
        // Shell_NotifyIcon P/Invoke here. We log and skip for now so the rest of the
        // app can come up. A proper tray icon is scheduled for the M6 polish milestone.
        _logger?.LogInformation("Tray icon requested (not yet implemented in WinUI 3 host)");
    }

    public void Dispose() { }
}
