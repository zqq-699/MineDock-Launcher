using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Launcher.App.Services;
using Launcher.App.Utilities;
using Launcher.App.ViewModels;

namespace Launcher.App.Views;

public partial class DownloadPageView : UserControl
{
    private readonly DownloadStepTransitionCoordinator stepTransition;
    private readonly DownloadVersionListView downloadVersionList;
    private INotifyPropertyChanged? currentViewModelNotifier;

    public DownloadPageView()
    {
        InitializeComponent();

        var downloadStepHost = FindDownloadStepHost();
        downloadVersionList = FindStepContent<DownloadVersionListView>(
            downloadStepHost,
            "DownloadVersionList",
            "Download version list view was not found.");

        stepTransition = new DownloadStepTransitionCoordinator(
            this,
            downloadStepHost,
            FindStepContent<FrameworkElement>(downloadStepHost, "VersionListStep", "Version list step was not found."),
            FindStepContent<FrameworkElement>(downloadStepHost, "InstanceOptionsStep", "Instance options step was not found."),
            FindFloatingButton("InstallStep"));

        Loaded += DownloadPageView_OnLoaded;
        DataContextChanged += DownloadPageView_OnDataContextChanged;
    }

    public FrameworkElement RootElement => PageRoot;

    private void DownloadPageView_OnLoaded(object sender, RoutedEventArgs e)
    {
        stepTransition.Sync(GetCurrentStep());
    }

    private void DownloadPageView_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged -= DownloadPageViewModel_OnPropertyChanged;

        currentViewModelNotifier = e.NewValue as INotifyPropertyChanged;
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged += DownloadPageViewModel_OnPropertyChanged;

        stepTransition.Sync(GetCurrentStep());
    }

    private void DownloadPageViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DownloadPageViewModel.SelectedVersionCategory)
            or nameof(DownloadPageViewModel.VersionSearchQuery))
        {
            ResetVersionListScrollPosition();
        }

        if (e.PropertyName is nameof(DownloadPageViewModel.ContentRefreshToken))
        {
            RefreshRightContentView();
        }

        if (e.PropertyName is nameof(DownloadPageViewModel.CurrentStep)
            && sender is DownloadPageViewModel viewModel)
        {
            stepTransition.AnimateTo(viewModel.CurrentStep);
        }
    }

    private void RefreshRightContentView()
    {
        stepTransition.Sync(GetCurrentStep());
        ResetVersionListScrollPosition();
    }

    private void ResetVersionListScrollPosition()
    {
        downloadVersionList.ScrollViewer.ScrollToVerticalOffset(0);
        downloadVersionList.RefreshViewport();
    }

    private void SecondaryMenuOptionButton_OnRefreshRequested(object sender, RoutedEventArgs e)
    {
        RefreshRightContentView();
    }

    private DownloadPageStep GetCurrentStep()
    {
        return DataContext is DownloadPageViewModel viewModel
            ? viewModel.CurrentStep
            : DownloadPageStep.VersionList;
    }

    private FrameworkElement FindDownloadStepHost()
    {
        return DownloadVersionListFrame.ListContent as FrameworkElement
            ?? throw new InvalidOperationException("Download step host content is not available.");
    }

    private static T FindStepContent<T>(DependencyObject root, string tag, string errorMessage)
        where T : FrameworkElement
    {
        return VisualTreeSearch.FindDescendant<T>(root, element => Equals(element.Tag, tag))
            ?? throw new InvalidOperationException(errorMessage);
    }

    private Button FindFloatingButton(string tag)
    {
        if (DownloadVersionListFrame.FloatingContent is not DependencyObject floatingContent)
            throw new InvalidOperationException("Download version floating content is not available.");

        return VisualTreeSearch.FindDescendant<Button>(floatingContent, button => Equals(button.Tag, tag))
            ?? throw new InvalidOperationException($"Download version floating button '{tag}' was not found.");
    }
}
