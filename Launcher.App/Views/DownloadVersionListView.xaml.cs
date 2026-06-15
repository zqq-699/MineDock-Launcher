using System.Windows;
using System.Windows.Controls;
using Launcher.App.Behaviors;
using Launcher.App.Controls;
using Launcher.App.Utilities;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

public partial class DownloadVersionListView : UserControl
{
    private const double VersionItemHeight = 58d;

    private ScrollViewer? scrollViewer;

    public DownloadVersionListView()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            AttachScrollViewer();
        };
    }

    public ScrollViewer ScrollViewer
    {
        get
        {
            AttachScrollViewer();
            return scrollViewer
                ?? throw new InvalidOperationException("Download version list scroll viewer is not available.");
        }
    }

    public double EstimatedVersionItemHeight => VersionItemHeight;

    public Button? FindVersionButton(DownloadMinecraftVersionItem selectedVersion)
    {
        if (DownloadVersionListBox.ItemContainerGenerator.ContainerFromItem(selectedVersion) is not ListBoxItem container)
            return null;

        return VisualTreeSearch.FindDescendant<ListPageItemButton>(container, _ => true)?.InnerButton;
    }

    public bool ContainsVersion(DownloadMinecraftVersionItem selectedVersion)
    {
        return DownloadVersionListBox.Items.Contains(selectedVersion);
    }

    public bool IsVersionRendered(DownloadMinecraftVersionItem selectedVersion)
    {
        return DownloadVersionListBox.ItemContainerGenerator.ContainerFromItem(selectedVersion) is ListBoxItem;
    }

    public bool RealizeVersion(DownloadMinecraftVersionItem selectedVersion)
    {
        if (!ContainsVersion(selectedVersion))
            return false;

        DownloadVersionListBox.ScrollIntoView(selectedVersion);
        DownloadVersionListBox.UpdateLayout();
        return IsVersionRendered(selectedVersion);
    }

    public double GetVersionTopOffset(DownloadMinecraftVersionItem selectedVersion)
    {
        var index = DownloadVersionListBox.Items.IndexOf(selectedVersion);
        return index >= 0 ? index * VersionItemHeight : 0;
    }

    public void RefreshViewport()
    {
        DownloadVersionListBox.UpdateLayout();
        VirtualizedListItemStateBehavior.Refresh(DownloadVersionListBox);
    }

    private void AttachScrollViewer()
    {
        DownloadVersionListBox.ApplyTemplate();

        var nextScrollViewer = VisualTreeSearch.FindDescendant<ScrollViewer>(DownloadVersionListBox, _ => true);
        if (ReferenceEquals(scrollViewer, nextScrollViewer))
            return;

        scrollViewer = nextScrollViewer;
    }
}
