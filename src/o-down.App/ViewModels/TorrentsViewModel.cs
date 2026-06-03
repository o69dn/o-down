using CommunityToolkit.Mvvm.ComponentModel;
using o_down.Core.Models;
using o_down.Data;
using System.Collections.ObjectModel;

namespace o_down.App.ViewModels;

public sealed partial class TorrentsViewModel : ObservableObject
{
    public TorrentsViewModel(OdownDbContext db)
    {
        Items = new ObservableCollection<DownloadItem>(
            db.Downloads.Local.Where(d => d.Kind == DownloadKind.Torrent).ToList());
    }

    public ObservableCollection<DownloadItem> Items { get; }
}
