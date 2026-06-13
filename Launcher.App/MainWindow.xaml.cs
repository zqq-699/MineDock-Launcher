using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Launcher.App.Controls;
using Launcher.App.Models;
using Launcher.App.Services;
using Launcher.App.ViewModels;

namespace Launcher.App;

public partial class MainWindow : Window
{
    public static readonly DependencyProperty IsMenuExpandedProperty =
        DependencyProperty.Register(nameof(IsMenuExpanded), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    private readonly NavigationMenuController navigationMenuController;
    private readonly DialogOverlayController dialogOverlayController;
    private readonly AccountDialogController accountDialogController;
    private readonly PageTransitionController pageTransitionController;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        navigationMenuController = new NavigationMenuController(MenuColumn);
        dialogOverlayController = new DialogOverlayController(this, WindowContentLayer);
        accountDialogController = new AccountDialogController(
            () => (DataContext as MainViewModel)?.AccountPage,
            dialogOverlayController,
            AddAccountDialogHost,
            DeleteAccountDialogHost,
            RenameAccountDialogHost);
        pageTransitionController = new PageTransitionController(Dispatcher, ResolvePageRoot, viewModel.CurrentPage);
        DataContext = viewModel;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        AcrylicWindow.Enable(this);
        SizeChanged += (_, _) => QueueOpenDialogBlurRefresh();
        accountDialogController.AttachSizeInvalidation(QueueDialogBlurRefreshWhenIdle);
        AccountPageView.AddAccountRequested += HandleAddAccountRequested;
        AccountPageView.DeleteAccountRequested += HandleDeleteAccountRequested;
        AccountPageView.RenameAccountRequested += HandleRenameAccountRequested;
        Loaded += async (_, _) =>
        {
            await viewModel.InitializeCommand.ExecuteAsync(null);
            IsMenuExpanded = viewModel.IsMenuExpanded;
            navigationMenuController.SetExpanded(IsMenuExpanded);
            _ = Dispatcher.BeginInvoke(PrewarmTransientUi, DispatcherPriority.ContextIdle);
        };
    }

    public bool IsMenuExpanded
    {
        get => (bool)GetValue(IsMenuExpandedProperty);
        set => SetValue(IsMenuExpandedProperty, value);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMenu_Click(object sender, RoutedEventArgs e)
    {
        var nextState = !IsMenuExpanded;
        IsMenuExpanded = nextState;

        if (DataContext is MainViewModel viewModel)
            viewModel.IsMenuExpanded = nextState;

        navigationMenuController.AnimateExpanded(nextState);
    }

    private void Navigation_Click(object sender, RoutedEventArgs e)
    {
        AccountPageView.ResetTransientUi();
        if (sender is FrameworkElement { DataContext: NavigationItem item }
            && DataContext is MainViewModel viewModel)
            viewModel.SelectNavigationItem(item);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.CurrentPage)
            || sender is not MainViewModel viewModel)
            return;

        pageTransitionController.MoveTo(viewModel.CurrentPage);
    }

    private FrameworkElement? ResolvePageRoot(string page)
    {
        return string.Equals(page, "Account", StringComparison.OrdinalIgnoreCase)
            ? AccountPageView.RootElement
            : GeneralPageView.RootElement;
    }

    private void HandleRenameAccountRequested()
    {
        accountDialogController.ShowRenameAccountDialog();
    }

    private void HandleAddAccountRequested()
    {
        accountDialogController.ShowAddAccountDialog();
    }

    private void HandleDeleteAccountRequested(LauncherAccount account)
    {
        accountDialogController.ShowDeleteAccountDialog(account);
    }

    private void CancelAddAccount_Click(object sender, RoutedEventArgs e)
    {
        accountDialogController.CancelAddAccountDialog();
    }

    private void BackAddAccount_Click(object sender, RoutedEventArgs e)
    {
        accountDialogController.BackAddAccountDialog();
    }

    private async void ConfirmAddAccount_Click(object sender, RoutedEventArgs e)
    {
        await accountDialogController.ConfirmAddAccountDialogAsync();
    }

    private void CancelDeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        accountDialogController.CancelDeleteAccountDialog();
    }

    private async void ConfirmDeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        await accountDialogController.ConfirmDeleteAccountDialogAsync();
    }

    private void CancelRenameAccount_Click(object sender, RoutedEventArgs e)
    {
        accountDialogController.CancelRenameAccountDialog();
    }

    private async void ConfirmRenameAccount_Click(object sender, RoutedEventArgs e)
    {
        await accountDialogController.ConfirmRenameAccountDialogAsync();
    }

    private void QueueOpenDialogBlurRefresh()
    {
        accountDialogController.QueueOpenDialogBlurRefresh();
    }

    private void QueueDialogBlurRefreshWhenIdle()
    {
        if (!dialogOverlayController.IsSizeAnimating)
            QueueOpenDialogBlurRefresh();
    }

    private void PrewarmTransientUi()
    {
        BlurEffectWarmup.EnsureWarmed();
        accountDialogController.Prewarm();

        foreach (var comboBox in FindVisualChildren<AnimatedComboBox>(this))
            comboBox.ApplyTemplate();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typedChild)
                yield return typedChild;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
