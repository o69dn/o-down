using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using o_down.Core.Abstractions;
using o_down.Core.Models;
using o_down.Infrastructure;

namespace o_down.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISidecarManager _sidecars;
    private readonly IBandwidthProfileService _profiles;
    private readonly ILogger<SettingsViewModel>? _logger;

    [ObservableProperty] private string aria2Path = string.Empty;
    [ObservableProperty] private string ytDlpPath = string.Empty;
    [ObservableProperty] private string ffmpegPath = string.Empty;
    [ObservableProperty] private string dataDirectory = string.Empty;
    [ObservableProperty] private long? maxBandwidthBytesPerSecond;
    [ObservableProperty] private bool clipboardMonitorEnabled = true;

    public ObservableCollection<BandwidthProfile> Profiles { get; } = new();
    [ObservableProperty] private BandwidthProfile? selectedProfile;
    [ObservableProperty] private string newProfileName = string.Empty;
    [ObservableProperty] private long? newMaxDownloadBytes;
    [ObservableProperty] private long? newMaxUploadBytes;
    [ObservableProperty] private string profileStatusMessage = string.Empty;

    public SettingsViewModel(
        ISidecarManager sidecars,
        IBandwidthProfileService profiles,
        ILogger<SettingsViewModel>? logger = null)
    {
        _sidecars = sidecars;
        _profiles = profiles;
        _logger = logger;
        Aria2Path = _sidecars.Aria2Executable;
        YtDlpPath = _sidecars.YtDlpExecutable;
        FfmpegPath = _sidecars.FfmpegExecutable;
        DataDirectory = _sidecars.DataDirectory;

        _ = LoadProfilesAsync();
    }

    [RelayCommand]
    private async Task LoadProfilesAsync()
    {
        try
        {
            var list = await _profiles.GetAllAsync();
            Profiles.Clear();
            foreach (var p in list) Profiles.Add(p);
            SelectedProfile = Profiles.FirstOrDefault(p => p.Id == _profiles.ActiveProfile?.Id) ?? Profiles[0];
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load bandwidth profiles");
        }
    }

    partial void OnSelectedProfileChanged(BandwidthProfile? value)
    {
        if (value is null) return;
        _ = ApplyProfileAsync(value);
    }

    [RelayCommand]
    private async Task ApplyProfileAsync(BandwidthProfile profile)
    {
        try
        {
            await _profiles.SetActiveAsync(profile.Id);
            ProfileStatusMessage = $"Applied '{profile.Name}' to all engines";
        }
        catch (Exception ex)
        {
            ProfileStatusMessage = $"Failed: {ex.Message}";
            _logger?.LogWarning(ex, "Failed to apply profile {Name}", profile.Name);
        }
    }

    [RelayCommand]
    private async Task AddProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(NewProfileName)) return;
        try
        {
            var p = await _profiles.AddAsync(NewProfileName.Trim(), NewMaxDownloadBytes, NewMaxUploadBytes);
            Profiles.Add(p);
            NewProfileName = string.Empty;
            NewMaxDownloadBytes = null;
            NewMaxUploadBytes = null;
            ProfileStatusMessage = $"Added '{p.Name}'";
        }
        catch (Exception ex)
        {
            ProfileStatusMessage = $"Add failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RemoveProfileAsync(BandwidthProfile? profile)
    {
        if (profile is null || profile.IsBuiltIn) return;
        try
        {
            await _profiles.RemoveAsync(profile.Id);
            Profiles.Remove(profile);
            ProfileStatusMessage = $"Removed '{profile.Name}'";
        }
        catch (Exception ex)
        {
            ProfileStatusMessage = $"Remove failed: {ex.Message}";
        }
    }
}
