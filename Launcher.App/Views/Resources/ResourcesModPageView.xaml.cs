using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Launcher.App.Behaviors;
using Launcher.App.Services;
using Launcher.App.Utilities;
using Launcher.App.ViewModels.Resources;

namespace Launcher.App.Views.Resources;

public partial class ResourcesModPageView : UserControl
{
    private const double LoadMoreThreshold = 320d;
    private readonly SlidingContentTransitionCoordinator stepTransition;
    private readonly SlidingContentTransitionCoordinator detailsTransition;
    private ScrollViewer? scrollViewer;
    private ScrollViewer? versionScrollViewer;
    private INotifyPropertyChanged? currentViewModelNotifier;
    private bool isVersionAutoLoadQueued;

    public ResourcesModPageView()
    {
        InitializeComponent();

        stepTransition = new SlidingContentTransitionCoordinator(
            this,
            ModStepHost,
            ProjectListStep,
            ProjectDetailsStep);
        detailsTransition = new SlidingContentTransitionCoordinator(
            this,
            DetailsStepHost,
            InstallTargetStep,
            ProjectVersionsStep);

        Loaded += (_, _) =>
        {
            AttachScrollViewers();
            stepTransition.Sync(IsProjectContentStep());
            detailsTransition.Sync(IsProjectVersionsStep());
        };
        Unloaded += (_, _) =>
        {
            DetachScrollViewers();
            DetachViewModelNotifier();
        };
        DataContextChanged += ResourcesModPageView_OnDataContextChanged;
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

    private void ResourcesModPageView_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachViewModelNotifier();

        currentViewModelNotifier = e.NewValue as INotifyPropertyChanged;
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged += ResourcesModPageViewModel_OnPropertyChanged;

        stepTransition.Sync(IsProjectContentStep());
        detailsTransition.Sync(IsProjectVersionsStep());
    }

    private void AttachScrollViewers()
    {
        AttachScrollViewer();
        AttachVersionScrollViewer();
    }

    private void DetachViewModelNotifier()
    {
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged -= ResourcesModPageViewModel_OnPropertyChanged;

        currentViewModelNotifier = null;
    }

    private void ResourcesModPageViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ResourcesModPageViewModel.CurrentStep)
            && sender is ResourcesModPageViewModel viewModel)
        {
            stepTransition.AnimateTo(viewModel.CurrentStep is not ResourcesModPageStep.ProjectList);
            detailsTransition.AnimateTo(viewModel.CurrentStep is ResourcesModPageStep.ProjectVersions);
            if (viewModel.CurrentStep is ResourcesModPageStep.ProjectVersions)
            {
                AttachVersionScrollViewer();
                QueueVersionAutoLoadIfNeeded();
            }
        }

        if (sender is ResourcesModPageViewModel
            && e.PropertyName is nameof(ResourcesModPageViewModel.IsLoadingAvailableVersions)
                or nameof(ResourcesModPageViewModel.IsLoadingMoreAvailableVersions)
                or nameof(ResourcesModPageViewModel.HasMoreAvailableVersions)
                or nameof(ResourcesModPageViewModel.VisibleAvailableVersionCount))
        {
            QueueVersionAutoLoadIfNeeded();
        }
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

    private void AttachVersionScrollViewer()
    {
        ResourcesModVersionListBox.ApplyTemplate();

        var nextScrollViewer = VisualTreeSearch.FindDescendant<ScrollViewer>(ResourcesModVersionListBox, _ => true);
        if (ReferenceEquals(versionScrollViewer, nextScrollViewer))
            return;

        DetachVersionScrollViewer();
        versionScrollViewer = nextScrollViewer;
        if (versionScrollViewer is not null)
            versionScrollViewer.ScrollChanged += VersionScrollViewer_OnScrollChanged;
    }

    private void DetachScrollViewer()
    {
        if (scrollViewer is not null)
            scrollViewer.ScrollChanged -= ScrollViewer_OnScrollChanged;

        scrollViewer = null;
    }

    private void DetachVersionScrollViewer()
    {
        if (versionScrollViewer is not null)
            versionScrollViewer.ScrollChanged -= VersionScrollViewer_OnScrollChanged;

        versionScrollViewer = null;
    }

    private void DetachScrollViewers()
    {
        DetachScrollViewer();
        DetachVersionScrollViewer();
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

    private void VersionScrollViewer_OnScrollChanged(object sender, ScrollChangedEventArgs e)
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
            viewModel.BeginLoadMoreAvailableVersions();
    }

    private void QueueVersionAutoLoadIfNeeded()
    {
        if (isVersionAutoLoadQueued)
            return;

        isVersionAutoLoadQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            isVersionAutoLoadQueued = false;
            BeginVersionAutoLoadIfNeeded();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void BeginVersionAutoLoadIfNeeded()
    {
        if (DataContext is not ResourcesModPageViewModel viewModel
            || viewModel.CurrentStep is not ResourcesModPageStep.ProjectVersions
            || !viewModel.HasMoreAvailableVersions
            || viewModel.IsLoadingAvailableVersions
            || viewModel.IsLoadingMoreAvailableVersions)
        {
            return;
        }

        AttachVersionScrollViewer();
        if (versionScrollViewer is null)
            return;

        ResourcesModVersionListBox.UpdateLayout();
        if (versionScrollViewer.ScrollableHeight <= 0)
            viewModel.BeginLoadMoreAvailableVersions();
    }

    private bool IsProjectContentStep()
    {
        return DataContext is ResourcesModPageViewModel viewModel
            && viewModel.CurrentStep is not ResourcesModPageStep.ProjectList;
    }

    private bool IsProjectVersionsStep()
    {
        return DataContext is ResourcesModPageViewModel viewModel
            && viewModel.CurrentStep is ResourcesModPageStep.ProjectVersions;
    }

}
