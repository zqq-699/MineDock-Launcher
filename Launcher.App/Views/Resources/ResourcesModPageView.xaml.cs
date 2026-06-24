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
    private INotifyPropertyChanged? currentViewModelNotifier;

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
            AttachScrollViewer();
            stepTransition.Sync(IsProjectContentStep());
            detailsTransition.Sync(IsProjectVersionsStep());
        };
        Unloaded += (_, _) =>
        {
            DetachScrollViewer();
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
