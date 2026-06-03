using CommunityToolkit.Mvvm.ComponentModel;
using o_down.Core.Abstractions;
using System.Collections.ObjectModel;

namespace o_down.App.ViewModels;

public sealed partial class QueueViewModel : ObservableObject
{
    private readonly IDownloadQueue _queue;
    public QueueViewModel(IDownloadQueue queue)
    {
        _queue = queue;
        Items = new ObservableCollection<Core.Models.DownloadItem>(_queue.Snapshot());
    }

    public ObservableCollection<Core.Models.DownloadItem> Items { get; }
}
