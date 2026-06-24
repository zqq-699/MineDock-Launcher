using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Launcher.App.Services;
using Launcher.App.Utilities;
using Launcher.App.ViewModels.Resources;

namespace Launcher.App.Views.Resources;

public partial class ResourcesPageView : UserControl
{
    private static readonly string[] SectionOrder =
    [
        "mods",
        "resource_packs",
        "shader_packs",
        "worlds",
        "modpacks"
    ];

    private readonly PageTransitionService sectionTransitionService;
    private INotifyPropertyChanged? currentViewModelNotifier;
    private FrameworkElement? sectionContentRoot;

    public ResourcesPageView()
    {
        InitializeComponent();
        sectionTransitionService = new PageTransitionService(
            Dispatcher,
            _ => sectionContentRoot,
            GetCurrentSectionId(),
            SectionOrder);

        Loaded += ResourcesPageView_Loaded;
        DataContextChanged += ResourcesPageView_DataContextChanged;
    }

    public FrameworkElement RootElement => PageRoot;

    private void ResourcesPageView_Loaded(object sender, RoutedEventArgs e)
    {
        sectionTransitionService.SyncTo(GetCurrentSectionId());
        ResetSectionPresentation();
    }

    private void ResourcesPageView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged -= ResourcesPageViewModel_PropertyChanged;

        currentViewModelNotifier = e.NewValue as INotifyPropertyChanged;
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged += ResourcesPageViewModel_PropertyChanged;

        sectionTransitionService.SyncTo(GetCurrentSectionId());
        ResetSectionPresentation();
    }

    private void ResourcesPageViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ResourcesPageViewModel.SelectedSection)
            && sender is ResourcesPageViewModel viewModel)
        {
            if (sectionContentRoot is null)
                return;

            sectionContentRoot.UpdateLayout();
            ResetCurrentSectionScrollPosition();
            sectionTransitionService.MoveTo(viewModel.SelectedSection?.Id ?? SectionOrder[0]);
        }
    }

    private void ResetCurrentSectionScrollPosition()
    {
        if (sectionContentRoot is null)
            return;

        var modPageView = VisualTreeSearch.FindDescendant<ResourcesModPageView>(sectionContentRoot, _ => true);
        if (modPageView is null)
            return;

        try
        {
            modPageView.ScrollViewer.ScrollToVerticalOffset(0);
            modPageView.RefreshViewport();
        }
        catch (InvalidOperationException)
        {
            // The virtualized list template may not be available before the Mod section is realized.
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
        return (DataContext as ResourcesPageViewModel)?.SelectedSection?.Id ?? SectionOrder[0];
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
