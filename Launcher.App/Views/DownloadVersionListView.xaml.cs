using System.Windows;
using System.Windows.Controls;
using Launcher.App.Controls;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

public partial class DownloadVersionListView : UserControl
{
    public DownloadVersionListView()
    {
        InitializeComponent();
    }

    public Button? FindVersionButton(DownloadMinecraftVersionItem selectedVersion)
    {
        return DownloadVersionItemsControl.FindRenderedButton(selectedVersion);
    }

    public bool ContainsVersion(DownloadMinecraftVersionItem selectedVersion)
    {
        return DownloadVersionItemsControl.ContainsItem(selectedVersion);
    }

    public bool IsVersionRendered(DownloadMinecraftVersionItem selectedVersion)
    {
        return DownloadVersionItemsControl.IsItemRendered(selectedVersion);
    }

    public double GetVersionTopOffset(DownloadMinecraftVersionItem selectedVersion)
    {
        return DownloadVersionItemsControl.GetItemTopOffset(selectedVersion);
    }

    public void RefreshViewport()
    {
        DownloadVersionItemsControl.RefreshViewport();
    }

    public double EstimatedVersionItemHeight => DownloadVersionItemsControl.EstimatedItemHeight;
}
