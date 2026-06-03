using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using o_down.App.ViewModels;
using Windows.System;

namespace o_down.App.Views;

public sealed partial class DownloadsPage : Page
{
    public DownloadsViewModel ViewModel { get; } = App.Services.GetRequiredService<DownloadsViewModel>();
    public DownloadsPage() => InitializeComponent();

    private void OnAddKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.AddLinkCommand.CanExecute(null))
        {
            ViewModel.AddLinkCommand.Execute(null);
        }
    }
}
