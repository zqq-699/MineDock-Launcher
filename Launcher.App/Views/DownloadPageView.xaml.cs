using System.Windows;
using System.Windows.Controls;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

public partial class DownloadPageView : UserControl
{
    public DownloadPageView()
    {
        InitializeComponent();
    }

    public FrameworkElement RootElement => PageRoot;

    private void DownloadVersionItem_OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (DataContext is DownloadPageViewModel viewModel && sender is FrameworkElement { DataContext: DownloadMinecraftVersionItem item })
            viewModel.SetHoveredMinecraftVersion(item);
    }

    private void DownloadVersionItem_OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (DataContext is DownloadPageViewModel viewModel)
            viewModel.SetHoveredMinecraftVersion(null);
    }
}
