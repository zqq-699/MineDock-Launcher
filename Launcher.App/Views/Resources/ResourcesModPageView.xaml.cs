using System.Windows.Controls;
using Launcher.App.Behaviors;
using Launcher.App.Utilities;
using Launcher.App.ViewModels.Resources;

namespace Launcher.App.Views.Resources;

public partial class ResourcesModPageView : UserControl
{
    private const double LoadMoreThreshold = 320d;
    private ScrollViewer? scrollViewer;

    public ResourcesModPageView()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            AttachScrollViewer();
        };
        Unloaded += (_, _) => DetachScrollViewer();
    }

    public ScrollViewer ScrollViewer
    {
        get
        {
            AttachScrollViewer();
            return scrollViewer
                ?? throw new InvalidOperationException("Resources mod list scroll viewer is not available.");
        }
    }

    public void RefreshViewport()
    {
        ResourcesModListBox.UpdateLayout();
        VirtualizedListItemStateBehavior.Refresh(ResourcesModListBox);
    }

    private void AttachScrollViewer()
    {
        ResourcesModListBox.ApplyTemplate();

        var nextScrollViewer = VisualTreeSearch.FindDescendant<ScrollViewer>(ResourcesModListBox, _ => true);
        if (ReferenceEquals(scrollViewer, nextScrollViewer))
            return;

        DetachScrollViewer();
        scrollViewer = nextScrollViewer;
        if (scrollViewer is not null)
            scrollViewer.ScrollChanged += ScrollViewer_OnScrollChanged;
    }

    private void DetachScrollViewer()
    {
        if (scrollViewer is not null)
            scrollViewer.ScrollChanged -= ScrollViewer_OnScrollChanged;

        scrollViewer = null;
    }

    private void ScrollViewer_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (DataContext is not ResourcesModPageViewModel viewModel
            || sender is not ScrollViewer currentScrollViewer)
        {
            return;
        }

        if (currentScrollViewer.ScrollableHeight <= 0)
            return;

        var remainingDistance = currentScrollViewer.ScrollableHeight - currentScrollViewer.VerticalOffset;
        if (remainingDistance <= LoadMoreThreshold)
            viewModel.BeginLoadMoreProjects();
    }
}
