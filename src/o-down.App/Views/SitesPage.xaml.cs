using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using o_down.App.ViewModels;

namespace o_down.App.Views;

public sealed partial class SitesPage : Page
{
    public SitesViewModel ViewModel { get; } = App.Services.GetRequiredService<SitesViewModel>();
    public SitesPage() => InitializeComponent();
}
