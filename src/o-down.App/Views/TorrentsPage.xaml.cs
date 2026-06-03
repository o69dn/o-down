using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using o_down.App.ViewModels;

namespace o_down.App.Views;

public sealed partial class TorrentsPage : Page
{
    public TorrentsViewModel ViewModel { get; } = App.Services.GetRequiredService<TorrentsViewModel>();
    public TorrentsPage() => InitializeComponent();
}
