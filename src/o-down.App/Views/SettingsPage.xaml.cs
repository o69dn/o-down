using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using o_down.App.ViewModels;

namespace o_down.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; } = App.Services.GetRequiredService<SettingsViewModel>();
    public SettingsPage() => InitializeComponent();
}
