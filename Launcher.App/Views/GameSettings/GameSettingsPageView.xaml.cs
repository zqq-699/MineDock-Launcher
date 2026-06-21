using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Launcher.App.Behaviors;
using Launcher.App.Services;
using Launcher.App.Utilities;

namespace Launcher.App.Views.GameSettings;

public partial class GameSettingsPageView : UserControl
{
    private const double ListLayerBaseTopFadeLength = 132d;
    private const double ListLayerBaseTopIntermediateLength = 0d;
    private const double ListLayerBaseTopIntermediateOpacity = 1d;
    private const double ListLayerBaseTopPlateauLength = 0d;
    private const double ListLayerBaseMinimumOpacity = 0d;
    private const double StickyModListTopFadeLength = 170d;
    private const double StickyModListTopIntermediateLength = 120d;
    private const double StickyModListTopIntermediateOpacity = 0.1d;
    private const double StickyModListTopPlateauLength = 0d;
    private const double StickyModListMinimumOpacity = 0d;
    private const double StickyModHeaderSpacing = 10d;
    private const double StickyModHeaderBottomPadding = 12d;

    private readonly SlidingContentTransitionCoordinator stepTransition;
    private SlidingContentTransitionCoordinator? secondaryMenuTransition;
    private INotifyPropertyChanged? currentViewModelNotifier;
    private INotifyPropertyChanged? currentDetailsNotifier;
    private readonly DispatcherTimer memoryRefreshTimer;
    private bool isWaitingForSecondaryMenuTransition;
    private bool isWaitingForStickyModHeader;
    private ScrollViewer? detailsScrollViewer;
    private InstanceModManagementSettingsView? currentModManagementView;
    private Grid? stickyModListFloatingLayer;
    private Border? stickyModListFloatingHost;
    private ContentControl? stickyModListFloatingContent;

    public GameSettingsPageView()
    {
        InitializeComponent();

        var stepHost = FindStepHost();
        stepTransition = new SlidingContentTransitionCoordinator(
            this,
            stepHost,
            FindStepContent<FrameworkElement>(stepHost, "InstanceListStep", "Instance list step was not found."),
            FindStepContent<FrameworkElement>(stepHost, "InstanceDetailsStep", "Instance details step was not found."));

        Loaded += GameSettingsPageView_Loaded;
        Unloaded += GameSettingsPageView_Unloaded;
        DataContextChanged += GameSettingsPageView_DataContextChanged;
        SizeChanged += GameSettingsPageView_SizeChanged;
        memoryRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        memoryRefreshTimer.Tick += MemoryRefreshTimer_Tick;
    }

    public FrameworkElement RootElement => PageRoot;

    internal static StickyModHeaderLayout CalculateStickyModHeaderLayout(
        double anchorTop,
        double originalHeaderTop,
        double sectionBottom,
        double overlayHeight)
    {
        if (double.IsNaN(anchorTop)
            || double.IsNaN(originalHeaderTop)
            || double.IsNaN(sectionBottom)
            || double.IsNaN(overlayHeight)
            || double.IsInfinity(anchorTop)
            || double.IsInfinity(originalHeaderTop)
            || double.IsInfinity(sectionBottom)
            || double.IsInfinity(overlayHeight)
            || overlayHeight <= 0
            || sectionBottom <= anchorTop
            || originalHeaderTop > anchorTop)
        {
            return StickyModHeaderLayout.Hidden;
        }

        var desiredTop = Math.Min(anchorTop, sectionBottom - overlayHeight);
        return new StickyModHeaderLayout(
            IsVisible: true,
            TranslateY: desiredTop);
    }

    private void GameSettingsPageView_Loaded(object sender, RoutedEventArgs e)
    {
        stepTransition.Sync(IsDetailsStep());
        EnsureSecondaryMenuTransition();
        secondaryMenuTransition?.Sync(IsDetailsStep());
        EnsureStickyModHeaderTracking();
        Dispatcher.BeginInvoke(UpdateStickyModHeaderState, DispatcherPriority.Loaded);
        RefreshMemorySnapshot();
        memoryRefreshTimer.Start();
    }

    private void GameSettingsPageView_Unloaded(object sender, RoutedEventArgs e)
    {
        memoryRefreshTimer.Stop();
        StopWaitingForStickyModHeader();
        ResetStickyModHeaderState();
        DetachStickyModHeaderTracking();
    }

    private void MemoryRefreshTimer_Tick(object? sender, EventArgs e)
    {
        RefreshMemorySnapshot();
    }

    private void RefreshMemorySnapshot()
    {
        if (DataContext is GameSettingsPageViewModel viewModel)
            viewModel.Details.RefreshSystemMemorySnapshot();
    }

    private void GameSettingsPageView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged -= GameSettingsPageViewModel_PropertyChanged;

        if (currentDetailsNotifier is not null)
            currentDetailsNotifier.PropertyChanged -= GameSettingsDetailsViewModel_PropertyChanged;

        currentViewModelNotifier = e.NewValue as INotifyPropertyChanged;
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged += GameSettingsPageViewModel_PropertyChanged;

        currentDetailsNotifier = (e.NewValue as GameSettingsPageViewModel)?.Details;
        if (currentDetailsNotifier is not null)
            currentDetailsNotifier.PropertyChanged += GameSettingsDetailsViewModel_PropertyChanged;

        stepTransition.Sync(IsDetailsStep());
        EnsureSecondaryMenuTransition();
        secondaryMenuTransition?.Sync(IsDetailsStep());
        EnsureStickyModHeaderTracking();
        Dispatcher.BeginInvoke(UpdateStickyModHeaderState, DispatcherPriority.Loaded);
    }

    private void GameSettingsPageViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GameSettingsPageViewModel.CurrentStep)
            && sender is GameSettingsPageViewModel viewModel)
        {
            EnsureSecondaryMenuTransition();
            stepTransition.AnimateTo(viewModel.CurrentStep is GameSettingsPageStep.Details);
            secondaryMenuTransition?.AnimateTo(viewModel.CurrentStep is GameSettingsPageStep.Details);
            EnsureStickyModHeaderTracking();
            Dispatcher.BeginInvoke(UpdateStickyModHeaderState, DispatcherPriority.Loaded);
        }
    }

    private void GameSettingsDetailsViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GameSettingsDetailsViewModel.SelectedSection)
            or nameof(GameSettingsDetailsViewModel.CurrentSectionViewModel))
        {
            EnsureStickyModHeaderTracking();
            Dispatcher.BeginInvoke(UpdateStickyModHeaderState, DispatcherPriority.Loaded);
        }
    }

    private void GameSettingsPageView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateStickyModHeaderState();
    }

    private void EnsureSecondaryMenuTransition()
    {
        if (secondaryMenuTransition is not null)
        {
            if (isWaitingForSecondaryMenuTransition)
            {
                LayoutUpdated -= GameSettingsPageView_LayoutUpdated;
                isWaitingForSecondaryMenuTransition = false;
            }
            return;
        }

        var secondaryMenuHost = TryFindStepContent<FrameworkElement>(this, "SecondaryMenuStepHost");
        if (secondaryMenuHost is null)
        {
            WaitForSecondaryMenuTransition();
            return;
        }

        var instanceCategoryMenuLayer = TryFindStepContent<FrameworkElement>(secondaryMenuHost, "InstanceCategoryMenuLayer");
        var detailsSectionMenuLayer = TryFindStepContent<FrameworkElement>(secondaryMenuHost, "DetailsSectionMenuLayer");
        if (instanceCategoryMenuLayer is null || detailsSectionMenuLayer is null)
        {
            WaitForSecondaryMenuTransition();
            return;
        }

        secondaryMenuTransition = new SlidingContentTransitionCoordinator(
            this,
            secondaryMenuHost,
            instanceCategoryMenuLayer,
            detailsSectionMenuLayer,
            useSlideTransition: false,
            useScaleTransition: true,
            transitionScale: 0.96);
        secondaryMenuTransition.Sync(IsDetailsStep());

        if (isWaitingForSecondaryMenuTransition)
        {
            LayoutUpdated -= GameSettingsPageView_LayoutUpdated;
            isWaitingForSecondaryMenuTransition = false;
        }
    }

    private void WaitForSecondaryMenuTransition()
    {
        if (isWaitingForSecondaryMenuTransition)
            return;

        LayoutUpdated += GameSettingsPageView_LayoutUpdated;
        isWaitingForSecondaryMenuTransition = true;
    }

    private void GameSettingsPageView_LayoutUpdated(object? sender, EventArgs e)
    {
        EnsureSecondaryMenuTransition();
    }

    private void StickyModHeader_LayoutUpdated(object? sender, EventArgs e)
    {
        EnsureStickyModHeaderTracking();
    }

    private bool IsDetailsStep()
    {
        return DataContext is GameSettingsPageViewModel viewModel
            && viewModel.CurrentStep is GameSettingsPageStep.Details;
    }

    private bool IsModManagementSectionSelected()
    {
        return DataContext is GameSettingsPageViewModel viewModel
            && string.Equals(viewModel.Details.SelectedSection?.Id, "mod_management", StringComparison.Ordinal);
    }

    private void EnsureStickyModHeaderTracking()
    {
        if (!IsDetailsStep() || !IsModManagementSectionSelected())
        {
            StopWaitingForStickyModHeader();
            ResetStickyModHeaderState();
            DetachStickyModHeaderTracking();
            return;
        }

        var detailsView = VisualTreeSearch.FindDescendant<GameSettingsDetailsView>(this, _ => true);
        var modManagementView = VisualTreeSearch.FindDescendant<InstanceModManagementSettingsView>(this, _ => true);
        if (detailsView is null || modManagementView is null)
        {
            WaitForStickyModHeader();
            return;
        }

        StopWaitingForStickyModHeader();
        AttachStickyModHeaderTracking(detailsView, modManagementView);
    }

    private void AttachStickyModHeaderTracking(
        GameSettingsDetailsView detailsView,
        InstanceModManagementSettingsView modManagementView)
    {
        if (!ReferenceEquals(detailsScrollViewer, detailsView.ScrollViewerControl))
        {
            DetachDetailsScrollViewer();
            detailsScrollViewer = detailsView.ScrollViewerControl;
            detailsScrollViewer.ScrollChanged += DetailsScrollViewer_ScrollChanged;
            detailsScrollViewer.SizeChanged += DetailsScrollViewer_SizeChanged;
        }

        if (!ReferenceEquals(currentModManagementView, modManagementView))
        {
            DetachModManagementView();
            currentModManagementView = modManagementView;
            currentModManagementView.SizeChanged += ModManagementView_SizeChanged;
            currentModManagementView.OriginalModListHeaderElement.SizeChanged += ModManagementView_SizeChanged;
            currentModManagementView.ModListSectionElement.SizeChanged += ModManagementView_SizeChanged;
        }

        if (!EnsureStickyFloatingElements())
            return;

        stickyModListFloatingContent!.Content = modManagementView.DataContext;
        stickyModListFloatingContent.ContentTemplate = modManagementView.ModListHeaderTemplate;
    }

    private void DetachStickyModHeaderTracking()
    {
        DetachDetailsScrollViewer();
        DetachModManagementView();

        if (!EnsureStickyFloatingElements())
            return;

        stickyModListFloatingContent!.Content = null;
        stickyModListFloatingContent.ContentTemplate = null;
    }

    private void DetachDetailsScrollViewer()
    {
        if (detailsScrollViewer is null)
            return;

        detailsScrollViewer.ScrollChanged -= DetailsScrollViewer_ScrollChanged;
        detailsScrollViewer.SizeChanged -= DetailsScrollViewer_SizeChanged;
        detailsScrollViewer = null;
    }

    private void DetachModManagementView()
    {
        if (currentModManagementView is null)
            return;

        currentModManagementView.SizeChanged -= ModManagementView_SizeChanged;
        currentModManagementView.OriginalModListHeaderElement.SizeChanged -= ModManagementView_SizeChanged;
        currentModManagementView.ModListSectionElement.SizeChanged -= ModManagementView_SizeChanged;
        currentModManagementView = null;
    }

    private void WaitForStickyModHeader()
    {
        if (isWaitingForStickyModHeader)
            return;

        LayoutUpdated += StickyModHeader_LayoutUpdated;
        isWaitingForStickyModHeader = true;
    }

    private void StopWaitingForStickyModHeader()
    {
        if (!isWaitingForStickyModHeader)
            return;

        LayoutUpdated -= StickyModHeader_LayoutUpdated;
        isWaitingForStickyModHeader = false;
    }

    private void DetailsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateStickyModHeaderState();
    }

    private void DetailsScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateStickyModHeaderState();
    }

    private void ModManagementView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateStickyModHeaderState();
    }

    private void UpdateStickyModHeaderState()
    {
        if (!IsLoaded
            || !IsDetailsStep()
            || !IsModManagementSectionSelected()
            || detailsScrollViewer is null
            || currentModManagementView is null
            || !EnsureStickyFloatingElements())
        {
            ResetStickyModHeaderState();
            return;
        }

        var originalHeaderHeight = currentModManagementView.OriginalModListHeaderElement.ActualHeight;
        var overlayHeight = originalHeaderHeight + StickyModHeaderBottomPadding;

        if (overlayHeight <= 0
            || originalHeaderHeight <= 0
            || currentModManagementView.ModListSectionElement.ActualHeight <= 0)
        {
            ResetStickyModHeaderState();
            return;
        }

        try
        {
            var anchorTop = GameSettingsListFrame.HeaderTitleRowElement.TranslatePoint(
                new Point(0, GameSettingsListFrame.HeaderTitleRowElement.ActualHeight),
                GameSettingsListFrame).Y + StickyModHeaderSpacing;
            var originalHeaderTop = currentModManagementView.OriginalModListHeaderElement.TranslatePoint(
                new Point(0, 0),
                GameSettingsListFrame).Y;
            var sectionBottom = currentModManagementView.ModListSectionElement.TranslatePoint(
                new Point(0, currentModManagementView.ModListSectionElement.ActualHeight),
                GameSettingsListFrame).Y;

            var layout = CalculateStickyModHeaderLayout(
                anchorTop,
                originalHeaderTop,
                sectionBottom,
                overlayHeight);

            ApplyStickyModHeaderLayout(layout);
        }
        catch (InvalidOperationException)
        {
            ResetStickyModHeaderState();
        }
    }

    private void ApplyStickyModHeaderLayout(StickyModHeaderLayout layout)
    {
        if (!EnsureStickyFloatingElements())
            return;

        stickyModListFloatingLayer!.Visibility = layout.IsVisible ? Visibility.Visible : Visibility.Hidden;
        stickyModListFloatingLayer.IsHitTestVisible = layout.IsVisible;
        EnsureStickyFloatingTransform().Y = layout.TranslateY;
        UpdateListLayerTopFadeLength(layout.IsVisible);

        if (currentModManagementView is null)
            return;

        currentModManagementView.OriginalModListHeaderElement.Opacity = layout.IsVisible ? 0d : 1d;
        currentModManagementView.OriginalModListHeaderElement.IsHitTestVisible = !layout.IsVisible;
    }

    private void ResetStickyModHeaderState()
    {
        if (EnsureStickyFloatingElements())
        {
            stickyModListFloatingLayer!.Visibility = Visibility.Hidden;
            stickyModListFloatingLayer.IsHitTestVisible = false;
            EnsureStickyFloatingTransform().Y = 0d;
        }

        UpdateListLayerTopFadeLength(isStickyVisible: false);

        if (currentModManagementView is null)
            return;

        currentModManagementView.OriginalModListHeaderElement.Opacity = 1d;
        currentModManagementView.OriginalModListHeaderElement.IsHitTestVisible = true;
    }

    private FrameworkElement FindStepHost()
    {
        return GameSettingsListFrame.ListContent as FrameworkElement
            ?? throw new InvalidOperationException("Game settings step host content is not available.");
    }

    private static T FindStepContent<T>(DependencyObject root, string tag, string errorMessage)
        where T : FrameworkElement
    {
        return TryFindStepContent<T>(root, tag)
            ?? throw new InvalidOperationException(errorMessage);
    }

    private static T? TryFindStepContent<T>(DependencyObject root, string tag)
        where T : FrameworkElement
    {
        return VisualTreeSearch.FindDescendant<T>(root, element => Equals(element.Tag, tag));
    }

    private bool EnsureStickyFloatingElements()
    {
        stickyModListFloatingLayer ??= VisualTreeSearch.FindDescendant<Grid>(
            GameSettingsListFrame,
            element => Equals(element.Tag, "StickyModListFloatingLayer"));
        stickyModListFloatingHost ??= VisualTreeSearch.FindDescendant<Border>(
            GameSettingsListFrame,
            element => Equals(element.Tag, "StickyModListFloatingHost"));
        stickyModListFloatingContent ??= VisualTreeSearch.FindDescendant<ContentControl>(
            GameSettingsListFrame,
            element => Equals(element.Tag, "StickyModListFloatingContent"));

        return stickyModListFloatingLayer is not null
            && stickyModListFloatingHost is not null
            && stickyModListFloatingContent is not null;
    }

    private TranslateTransform EnsureStickyFloatingTransform()
    {
        if (stickyModListFloatingHost?.RenderTransform is TranslateTransform transform)
            return transform;

        transform = new TranslateTransform();
        if (stickyModListFloatingHost is not null)
            stickyModListFloatingHost.RenderTransform = transform;
        return transform;
    }

    private void UpdateListLayerTopFadeLength(bool isStickyVisible)
    {
        var listLayer = GameSettingsListFrame.ListLayerElement;
        VerticalEdgeOpacityMask.SetTopFadeLength(
            listLayer,
            isStickyVisible ? StickyModListTopFadeLength : ListLayerBaseTopFadeLength);
        VerticalEdgeOpacityMask.SetTopIntermediateLength(
            listLayer,
            isStickyVisible ? StickyModListTopIntermediateLength : ListLayerBaseTopIntermediateLength);
        VerticalEdgeOpacityMask.SetTopIntermediateOpacity(
            listLayer,
            isStickyVisible ? StickyModListTopIntermediateOpacity : ListLayerBaseTopIntermediateOpacity);
        VerticalEdgeOpacityMask.SetTopPlateauLength(
            listLayer,
            isStickyVisible ? StickyModListTopPlateauLength : ListLayerBaseTopPlateauLength);
        VerticalEdgeOpacityMask.SetMinimumOpacity(
            listLayer,
            isStickyVisible ? StickyModListMinimumOpacity : ListLayerBaseMinimumOpacity);
    }
}

internal readonly record struct StickyModHeaderLayout(bool IsVisible, double TranslateY)
{
    public static StickyModHeaderLayout Hidden => new(false, 0d);
}
