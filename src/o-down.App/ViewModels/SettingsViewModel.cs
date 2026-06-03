using CommunityToolkit.Mvvm.ComponentModel;
using o_down.Core.Abstractions;

namespace o_down.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISidecarManager _sidecars;
    private readonly Microsoft.Extensions.Logging.ILogger<SettingsViewModel>? _logger;

    [ObservableProperty] private string aria2Path = string.Empty;
    [ObservableProperty] private string ytDlpPath = string.Empty;
    [ObservableProperty] private string ffmpegPath = string.Empty;
    [ObservableProperty] private string dataDirectory = string.Empty;
    [ObservableProperty] private long? maxBandwidthBytesPerSecond;
    [ObservableProperty] private bool clipboardMonitorEnabled = true;

    public SettingsViewModel(ISidecarManager sidecars, Microsoft.Extensions.Logging.ILogger<SettingsViewModel>? logger = null)
    {
        _sidecars = sidecars;
        _logger = logger;
        Aria2Path = _sidecars.Aria2Executable;
        YtDlpPath = _sidecars.YtDlpExecutable;
        FfmpegPath = _sidecars.FfmpegExecutable;
        DataDirectory = _sidecars.DataDirectory;
    }
}
