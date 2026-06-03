using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using o_down.App.Services;
using o_down.Core.Models;
using o_down.Data;

namespace o_down.App.ViewModels;

public sealed partial class DownloadsViewModel : ObservableObject
{
    private readonly OdownDbContext _db;
    private readonly LinkIngestService _ingest;
    private readonly DownloadOrchestrator _orchestrator;

    public DownloadsViewModel(OdownDbContext db, LinkIngestService ingest, DownloadOrchestrator orchestrator)
    {
        _db = db;
        _ingest = ingest;
        _orchestrator = orchestrator;
        _ = LoadAsync();
    }

    public ObservableCollection<DownloadItem> Items { get; } = new();

    [ObservableProperty] private string newLink = string.Empty;

    [RelayCommand]
    private async Task AddLinkAsync()
    {
        if (string.IsNullOrWhiteSpace(NewLink)) return;
        await _ingest.IngestBatchAsync(new[] { NewLink }, "ui");
        NewLink = string.Empty;
        await LoadAsync();
    }

    [RelayCommand]
    private void Pause(DownloadItem? item)
    {
        if (item is null) return;
        _ = _orchestrator.PauseAsync(item.Id);
    }

    [RelayCommand]
    private void Resume(DownloadItem? item)
    {
        if (item is null) return;
        _ = _orchestrator.ResumeAsync(item.Id);
    }

    [RelayCommand]
    private void OpenFolder(DownloadItem? item)
    {
        if (item is null) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{item.DestinationDirectory}\"") { UseShellExecute = true }); } catch { }
    }

    private async Task LoadAsync()
    {
        var rows = await _db.Downloads
            .Where(d => d.State != DownloadState.Completed && d.State != DownloadState.Removed)
            .OrderByDescending(d => d.Priority)
            .ThenBy(d => d.CreatedAt)
            .AsNoTracking()
            .ToListAsync();
        Items.Clear();
        foreach (var r in rows) Items.Add(r);
    }
}
