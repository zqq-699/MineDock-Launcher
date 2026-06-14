using System.Windows;
using System.Windows.Controls;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

public partial class AccountPageView : UserControl
{
    public AccountPageView()
    {
        InitializeComponent();
    }

    public FrameworkElement RootElement => PageRoot;

    private async void SecondaryMenuOptionButton_OnRefreshRequested(object sender, RoutedEventArgs e)
    {
        AccountDetailsScrollViewer.ScrollToTop();

        if (DataContext is AccountPageViewModel viewModel)
            await viewModel.RefreshCurrentSecondaryContentAsync();
    }
}
