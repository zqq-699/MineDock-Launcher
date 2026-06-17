using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        "java_memory",
        "mod_management",
        "saves",
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

    private void DescriptionTextBox_OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        QueueDescriptionTextBoxHeightUpdate();
    }

    private void DescriptionTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        QueueDescriptionTextBoxHeightUpdate();
    }

    private void DescriptionTextBox_OnSizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
            QueueDescriptionTextBoxHeightUpdate();
    }

    private void QueueDescriptionTextBoxHeightUpdate()
    {
        Dispatcher.BeginInvoke(UpdateDescriptionTextBoxHeight, DispatcherPriority.Background);
    }

    private void UpdateDescriptionTextBoxHeight()
    {
        if (DescriptionTextBox is null)
            return;

        DescriptionTextBox.UpdateLayout();
        var lineCount = Math.Max(1, DescriptionTextBox.LineCount);
        var lineHeight = Math.Max(
            DescriptionTextBox.FontFamily.LineSpacing * DescriptionTextBox.FontSize,
            DescriptionTextBox.FontSize * 1.35);
        var chromeAllowance = 18d;
        DescriptionTextBox.Height = Math.Max(40, Math.Ceiling((lineCount * lineHeight) + chromeAllowance));
    }

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
            DetailsScrollViewer.ScrollToVerticalOffset(0);
            DetailsScrollViewer.UpdateLayout();
            SectionContentRoot.UpdateLayout();
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
