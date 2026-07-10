using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Launcher.App.Services;
using Launcher.App.ViewModels.Settings;

namespace Launcher.App.Views.Settings;

public partial class SettingsPageView : UserControl
{
    private static readonly string[] SectionOrder =
    [
        nameof(SettingsPageSection.General),
        nameof(SettingsPageSection.Language),
        nameof(SettingsPageSection.LaunchMemory),
        nameof(SettingsPageSection.Java),
        nameof(SettingsPageSection.Theme),
        nameof(SettingsPageSection.ListPreview),
        nameof(SettingsPageSection.Info)
    ];

    private readonly DispatcherTimer memoryRefreshTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };
    private readonly PageTransitionService sectionTransitionService;
    private INotifyPropertyChanged? currentViewModelNotifier;
    private FrameworkElement? sectionContentRoot;

    public SettingsPageView()
    {
        InitializeComponent();
        sectionTransitionService = new PageTransitionService(
            Dispatcher,
            _ => sectionContentRoot,
            GetCurrentSectionId(),
            SectionOrder);
        memoryRefreshTimer.Tick += MemoryRefreshTimer_Tick;
        Loaded += SettingsPageView_Loaded;
        Unloaded += SettingsPageView_Unloaded;
        DataContextChanged += SettingsPageView_DataContextChanged;
    }

    public FrameworkElement RootElement => PageRoot;

    private void SettingsPageView_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshMemorySnapshot();
        sectionTransitionService.SyncTo(GetCurrentSectionId());
        ResetSectionPresentation();
        memoryRefreshTimer.Start();
    }

    private void SettingsPageView_Unloaded(object sender, RoutedEventArgs e)
    {
        memoryRefreshTimer.Stop();
    }

    private void MemoryRefreshTimer_Tick(object? sender, EventArgs e)
    {
        RefreshMemorySnapshot();
    }

    private void RefreshMemorySnapshot()
    {
        if (DataContext is SettingsPageViewModel viewModel)
            viewModel.RefreshSystemMemorySnapshot();
    }

    private void SettingsPageView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged -= SettingsPageViewModel_PropertyChanged;

        currentViewModelNotifier = e.NewValue as INotifyPropertyChanged;
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged += SettingsPageViewModel_PropertyChanged;

        sectionTransitionService.SyncTo(GetCurrentSectionId());
        ResetSectionPresentation();
    }

    private void SettingsPageViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsPageViewModel.SelectedSection)
            && sender is SettingsPageViewModel viewModel)
        {
            if (sectionContentRoot is null)
                return;

            SettingsListFrame.ScrollViewer.ScrollToVerticalOffset(0);
            SettingsListFrame.ScrollViewer.UpdateLayout();
            sectionContentRoot.UpdateLayout();
            sectionTransitionService.MoveTo(viewModel.SelectedSection?.Section.ToString() ?? SectionOrder[0]);
        }
    }

    private void ResetSectionPresentation()
    {
        if (sectionContentRoot is null)
            return;

        sectionContentRoot.BeginAnimation(OpacityProperty, null);
        sectionContentRoot.Opacity = 1;

        var transform = EnsureTranslateTransform();
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.Y = 0;
    }

    private TranslateTransform EnsureTranslateTransform()
    {
        if (sectionContentRoot?.RenderTransform is TranslateTransform transform)
            return transform;

        transform = new TranslateTransform();
        if (sectionContentRoot is not null)
            sectionContentRoot.RenderTransform = transform;
        return transform;
    }

    private string GetCurrentSectionId()
    {
        return (DataContext as SettingsPageViewModel)?.SelectedSection?.Section.ToString() ?? SectionOrder[0];
    }

    private void SectionContentRoot_OnLoaded(object sender, RoutedEventArgs e)
    {
        sectionContentRoot = sender as FrameworkElement;
        sectionTransitionService.SyncTo(GetCurrentSectionId());
        ResetSectionPresentation();
    }

    private void SectionContentRoot_OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sectionContentRoot, sender))
            sectionContentRoot = null;
    }
}
