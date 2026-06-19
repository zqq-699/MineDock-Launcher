using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Launcher.App.Services;
using Launcher.App.ViewModels.Account;

namespace Launcher.App.Views.Account;

public partial class AccountDetailsView : UserControl
{
    private readonly PageTransitionService accountTransitionService;
    private INotifyPropertyChanged? currentViewModelNotifier;
    private string? currentAccountToken;

    public AccountDetailsView()
    {
        InitializeComponent();

        accountTransitionService = new PageTransitionService(
            Dispatcher,
            _ => DetailsContentRoot,
            GetCurrentAccountToken());

        Loaded += AccountDetailsView_Loaded;
        DataContextChanged += AccountDetailsView_DataContextChanged;
    }

    public void ScrollToTop()
    {
        AccountDetailsScrollViewer.ScrollToTop();
    }

    private void AccountDetailsView_Loaded(object sender, RoutedEventArgs e)
    {
        currentAccountToken = GetCurrentAccountToken();
        accountTransitionService.SyncTo(currentAccountToken);
        ResetContentPresentation();
    }

    private void AccountDetailsView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged -= AccountPageViewModel_PropertyChanged;

        currentViewModelNotifier = e.NewValue as INotifyPropertyChanged;
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged += AccountPageViewModel_PropertyChanged;

        currentAccountToken = GetCurrentAccountToken();
        accountTransitionService.SyncTo(currentAccountToken);
        ResetContentPresentation();
    }

    private void AccountPageViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(AccountPageViewModel.SelectedAccount))
            return;

        var accountToken = GetCurrentAccountToken();
        if (string.IsNullOrWhiteSpace(accountToken))
        {
            currentAccountToken = null;
            accountTransitionService.SyncTo(null);
            ResetContentPresentation();
            return;
        }

        if (string.Equals(currentAccountToken, accountToken, StringComparison.Ordinal))
            return;

        currentAccountToken = accountToken;
        ScrollToTop();
        Dispatcher.BeginInvoke(
            () => accountTransitionService.MoveTo(accountToken),
            DispatcherPriority.Loaded);
    }

    private string? GetCurrentAccountToken()
    {
        return (DataContext as AccountPageViewModel)?.SelectedAccount?.Id;
    }

    private void ResetContentPresentation()
    {
        DetailsContentRoot.BeginAnimation(OpacityProperty, null);
        DetailsContentRoot.Opacity = 1;

        var transform = EnsureTranslateTransform();
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.Y = 0;
    }

    private TranslateTransform EnsureTranslateTransform()
    {
        if (DetailsContentRoot.RenderTransform is TranslateTransform transform)
            return transform;

        transform = new TranslateTransform();
        DetailsContentRoot.RenderTransform = transform;
        return transform;
    }

}
