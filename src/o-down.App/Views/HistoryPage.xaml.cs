using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using o_down.App.ViewModels;

namespace o_down.App.Views;

public sealed partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; } = App.Services.GetRequiredService<HistoryViewModel>();
    public HistoryPage() => InitializeComponent();
}
