using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using o_down.Core.Models;
using o_down.Data;
using System.Collections.ObjectModel;

namespace o_down.App.ViewModels;

public sealed partial class HistoryViewModel : ObservableObject
{
    public HistoryViewModel(OdownDbContext db)
    {
        _ = LoadAsync(db);
    }

    public ObservableCollection<DownloadItem> Items { get; } = new();

    private async Task LoadAsync(OdownDbContext db)
    {
        var rows = await db.Downloads
            .Where(d => d.State == DownloadState.Completed || d.State == DownloadState.Failed)
            .OrderByDescending(d => d.CompletedAt ?? d.CreatedAt)
            .AsNoTracking()
            .Take(500)
            .ToListAsync();
        Items.Clear();
        foreach (var r in rows) Items.Add(r);
    }
}
