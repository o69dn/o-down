using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using o_down.App.Services;
using o_down.App.Views;

namespace o_down.App;

public sealed partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        var tag = item.Tag as string;
        var type = tag switch
        {
            "downloads" => typeof(DownloadsPage),
            "torrents"  => typeof(TorrentsPage),
            "queue"     => typeof(QueuePage),
            "history"   => typeof(HistoryPage),
            "sites"     => typeof(SitesPage),
            "settings"  => typeof(SettingsPage),
            _ => typeof(DownloadsPage)
        };
        ContentFrame.Navigate(type);
    }
}
