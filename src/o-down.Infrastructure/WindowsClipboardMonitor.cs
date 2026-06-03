using System.Runtime.InteropServices;
using o_down.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace o_down.Infrastructure;

public sealed class WindowsClipboardMonitor : IClipboardMonitor, IDisposable
{
    private readonly ILogger<WindowsClipboardMonitor>? _logger;
    private readonly HWND _hwnd;
    private readonly WndProcDelegate _wndProcDelegate;
    private bool _running;
    private bool _enabled = true;
    private string _lastText = string.Empty;
    private DateTimeOffset _lastFire = DateTimeOffset.MinValue;

    public WindowsClipboardMonitor(ILogger<WindowsClipboardMonitor>? logger = null)
    {
        _logger = logger;
        _wndProcDelegate = WndProc;
        var className = "o-down-clipboard-listener-" + Guid.NewGuid().ToString("N");
        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = GetModuleHandleW(null),
            lpszClassName = className
        };
        RegisterClassExW(ref wc);
        _hwnd = CreateWindowExW(
            0, className, "o-down-clipboard", 0,
            0, 0, 0, 0,
            HWND.Null, HMENU.Null, HINSTANCE.Null, nint.Zero);
    }

    public bool IsRunning => _running;
    public event EventHandler<string>? TextCaptured;

    public void Disable() => _enabled = false;
    public void Enable() => _enabled = true;

    public Task StartAsync(CancellationToken ct = default)
    {
        if (_running) return Task.CompletedTask;
        AddClipboardFormatListener(_hwnd);
        _running = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!_running) return Task.CompletedTask;
        RemoveClipboardFormatListener(_hwnd);
        _running = false;
        return Task.CompletedTask;
    }

    private nint WndProc(HWND hwnd, uint msg, nint wParam, nint lParam)
    {
        const uint WM_CLIPBOARDUPDATE = 0x031D;
        if (msg == WM_CLIPBOARDUPDATE && _enabled)
        {
            TryReadClipboard();
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void TryReadClipboard()
    {
        try
        {
            if (!OpenClipboard(HWND.Null)) return;
            try
            {
                if (!IsClipboardFormatAvailable(13)) return; // CF_UNICODETEXT
                var hGlobal = GetClipboardData(13);
                if (hGlobal == nint.Zero) return;
                var lpwcstr = GlobalLock(hGlobal);
                if (lpwcstr == nint.Zero) return;
                try
                {
                    var text = Marshal.PtrToStringUni(lpwcstr) ?? string.Empty;
                    var trimmed = text.Trim();
                    var now = DateTimeOffset.UtcNow;
                    if (string.IsNullOrEmpty(trimmed)) return;
                    if (trimmed == _lastText && (now - _lastFire) < TimeSpan.FromSeconds(2)) return;
                    if (!o_down.Core.Pipeline.UrlClassifier.IsUrl(trimmed)) return;
                    _lastText = trimmed;
                    _lastFire = now;
                    TextCaptured?.Invoke(this, trimmed);
                }
                finally
                {
                    GlobalUnlock(hGlobal);
                }
            }
            finally
            {
                CloseClipboard();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Clipboard read failed");
        }
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        if (_hwnd != HWND.Null) DestroyWindow(_hwnd);
    }

    private const uint CF_UNICODETEXT = 13;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public HINSTANCE hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct HWND { public nint Value { get; } public HWND(nint v) => Value = v; public static HWND Null => new(nint.Zero); public static implicit operator nint(HWND h) => h.Value; }
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct HMENU { public nint Value { get; } public HMENU(nint v) => Value = v; public static HMENU Null => new(nint.Zero); public static implicit operator nint(HMENU h) => h.Value; }
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct HINSTANCE { public nint Value { get; } public HINSTANCE(nint v) => Value = v; public static HINSTANCE Null => new(nint.Zero); public static implicit operator nint(HINSTANCE h) => h.Value; }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate nint WndProcDelegate(HWND hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW")]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")]
    private static extern HWND CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int X, int Y, int nWidth, int nHeight, HWND hWndParent, HMENU hMenu, HINSTANCE hInstance, nint lpParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(HWND hwnd);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(HWND hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(HWND hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(HWND hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(HWND hwndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetClipboardData(uint uFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint hMem);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern HINSTANCE GetModuleHandleW(string? lpModuleName);
}
