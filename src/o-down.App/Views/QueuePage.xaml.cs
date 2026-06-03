using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using o_down.App.ViewModels;

namespace o_down.App.Views;

public sealed partial class QueuePage : Page
{
    public QueueViewModel ViewModel { get; } = App.Services.GetRequiredService<QueueViewModel>();
    public QueuePage() => InitializeComponent();
}
