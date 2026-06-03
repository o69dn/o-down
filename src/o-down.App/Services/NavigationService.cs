using Microsoft.UI.Xaml.Controls;
using o_down.App.Views;

namespace o_down.App.Services;

public interface INavigationService
{
    void NavigateTo(string tag);
}

public sealed class NavigationService : INavigationService
{
    public void NavigateTo(string tag)
    {
        if (App.MainWindowRef?.Content is Grid grid &&
            grid.FindName("NavView") is NavigationView nav)
        {
            nav.SelectedItem = nav.MenuItems
                .OfType<NavigationViewItem>()
                .FirstOrDefault(i => (i.Tag as string) == tag);
        }
    }
}
