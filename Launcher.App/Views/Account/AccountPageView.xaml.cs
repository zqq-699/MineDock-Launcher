using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Launcher.App.Services;
using Launcher.App.ViewModels.Account;

namespace Launcher.App.Views.Account;

public partial class AccountPageView : UserControl
{
    private readonly SlidingContentTransitionCoordinator selectionTransition;
    private INotifyPropertyChanged? currentViewModelNotifier;

    public AccountPageView()
    {
        InitializeComponent();

        selectionTransition = new SlidingContentTransitionCoordinator(
            this,
            AccountContentHost,
            AccountEmptyStateView,
            AccountDetailsView);

        Loaded += AccountPageView_Loaded;
        DataContextChanged += AccountPageView_DataContextChanged;
    }

    public FrameworkElement RootElement => PageRoot;

    private async void SecondaryMenuOptionButton_OnRefreshRequested(object sender, RoutedEventArgs e)
    {
        AccountDetailsView.ScrollToTop();

        if (DataContext is AccountPageViewModel viewModel)
            await viewModel.Appearance.RefreshCurrentSecondaryContentAsync();
    }

    private void AccountPageView_Loaded(object sender, RoutedEventArgs e)
    {
        selectionTransition.Sync(HasSelectedAccount());
    }

    private void AccountPageView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged -= AccountPageViewModel_PropertyChanged;

        currentViewModelNotifier = e.NewValue as INotifyPropertyChanged;
        if (currentViewModelNotifier is not null)
            currentViewModelNotifier.PropertyChanged += AccountPageViewModel_PropertyChanged;

        selectionTransition.Sync(HasSelectedAccount());
    }

    private void AccountPageViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AccountPageViewModel.SelectedAccount))
            selectionTransition.AnimateTo(HasSelectedAccount());
    }

    private bool HasSelectedAccount()
    {
        return DataContext is AccountPageViewModel viewModel
            && viewModel.SelectedAccount is not null;
    }
}
