using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Threading;
using Launcher.App.Services;
using Launcher.App.ViewModels.GameSettings;

namespace Launcher.App.Views.GameSettings;

public partial class GameSettingsDetailsView : UserControl
{
    private static readonly string[] SectionOrder =
    [
        "general",
        "launch",
        "java",
        "mod_management",
        "saves",
        "resource_packs",
        "shaders",
        "loader",
        "advanced",
        "backup"
    ];

    private INotifyPropertyChanged? currentViewModelNotifier;
    private readonly PageTransitionService sectionTransitionService;

    public GameSettingsDetailsView()
    {
        InitializeComponent();
        sectionTransitionService = new PageTransitionService(
            Dispatcher,
            _ => SectionContentRoot,
            GetCurrentSectionId(),
            SectionOrder);

        Loaded += GameSettingsDetailsView_Loaded;
        DataContextChanged += GameSettingsDetailsView_DataContextChanged;
    }

    internal ScrollViewer ScrollViewerControl => DetailsScrollViewer;

    private void GameSettingsDetailsView_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        sectionTransitionService.SyncTo(GetCurrentSectionId());
        ResetSectionPresentation();
    }

    private void GameSettingsDetailsView_DataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged -= GameSettingsDetailsViewModel_PropertyChanged;

        currentViewModelNotifier = e.NewValue as INotifyPropertyChanged;
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged += GameSettingsDetailsViewModel_PropertyChanged;

        sectionTransitionService.SyncTo(GetCurrentSectionId());
        ResetSectionPresentation();
    }

    private void GameSettingsDetailsViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GameSettingsDetailsViewModel.SelectedSection)
            && sender is GameSettingsDetailsViewModel viewModel)
        {
            Dispatcher.BeginInvoke(
                () => DetailsScrollViewer.ScrollToVerticalOffset(0),
                DispatcherPriority.Background);
            sectionTransitionService.MoveTo(viewModel.SelectedSection?.Id ?? SectionOrder[0]);
        }
    }

    private void ResetSectionPresentation()
    {
        SectionContentRoot.BeginAnimation(OpacityProperty, null);
        SectionContentRoot.Opacity = 1;

        var transform = EnsureTranslateTransform();
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.Y = 0;
    }

    private TranslateTransform EnsureTranslateTransform()
    {
        if (SectionContentRoot.RenderTransform is TranslateTransform transform)
            return transform;

        transform = new TranslateTransform();
        SectionContentRoot.RenderTransform = transform;
        return transform;
    }

    private string GetCurrentSectionId()
    {
        return (DataContext as GameSettingsDetailsViewModel)?.SelectedSection?.Id ?? SectionOrder[0];
    }
}
