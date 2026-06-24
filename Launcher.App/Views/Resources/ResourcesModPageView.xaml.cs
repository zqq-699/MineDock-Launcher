using System.Windows.Controls;
using Launcher.App.Behaviors;
using Launcher.App.Utilities;

namespace Launcher.App.Views.Resources;

public partial class ResourcesModPageView : UserControl
{
    private ScrollViewer? scrollViewer;

    public ResourcesModPageView()
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

        scrollViewer = nextScrollViewer;
    }
}
