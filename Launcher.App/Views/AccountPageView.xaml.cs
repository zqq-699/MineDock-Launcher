using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Launcher.App.Models;
using Launcher.App.ViewModels;
using Microsoft.Win32;

namespace Launcher.App.Views;

public partial class AccountPageView : UserControl
{
    private AccountPageViewModel? observedViewModel;

    public AccountPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => DetachObservedViewModel();
    }

    public event Action? AddAccountRequested;
    public event Action<LauncherAccount>? DeleteAccountRequested;
    public event Action? RenameAccountRequested;

    public FrameworkElement RootElement => PageRoot;

    public void ResetTransientUi()
    {
        SetUuidCopyButtonCopied(false);
    }

    private AccountPageViewModel? ViewModel => DataContext as AccountPageViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachObservedViewModel();
        observedViewModel = e.NewValue as AccountPageViewModel;
        if (observedViewModel is not null)
            observedViewModel.PropertyChanged += ViewModel_PropertyChanged;

        ResetTransientUi();
    }

    private void DetachObservedViewModel()
    {
        if (observedViewModel is null)
            return;

        observedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        observedViewModel = null;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountPageViewModel.SelectedAccount))
            ResetTransientUi();
    }

    private void Account_Click(object sender, RoutedEventArgs e)
    {
        ResetTransientUi();
        if (sender is FrameworkElement { DataContext: LauncherAccount account })
            ViewModel?.SelectAccount(account);
    }

    private void CopyUuid_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedAccount is not { } account)
            return;

        var uuid = account.Uuid;
        if (string.IsNullOrWhiteSpace(uuid))
        {
            SetUuidCopyButtonCopied(true, account.Id);
            return;
        }

        try
        {
            Clipboard.SetDataObject(uuid, true);
            SetUuidCopyButtonCopied(true, account.Id);
        }
        catch (Exception ex)
        {
            ViewModel?.NotifyStatusMessage($"\u590d\u5236 UUID \u5931\u8d25\uff1a{ex.Message}");
            SetUuidCopyButtonCopied(false, account.Id);
        }
    }

    private void EditAccountName_Click(object sender, RoutedEventArgs e)
    {
        RenameAccountRequested?.Invoke();
    }

    private async void ChangeSkin_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || !viewModel.CanChangeSelectedAccountSkin)
            return;

        var window = Window.GetWindow(this);
        var dialog = new OpenFileDialog
        {
            Title = "\u9009\u62e9 Minecraft \u76ae\u80a4",
            Filter = "PNG \u76ae\u80a4 (*.png)|*.png",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(window) == true)
            await viewModel.ChangeSelectedAccountSkinAsync(dialog.FileName);
    }

    private async void RefreshCapes_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
            await viewModel.RefreshSelectedAccountProfileAsync();
    }

    private async void ApplyCape_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
            await viewModel.ApplySelectedAccountCapeAsync();
    }

    private void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        AddAccountRequested?.Invoke();
    }

    private void DeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: LauncherAccount account })
            DeleteAccountRequested?.Invoke(account);
    }

    private void SetUuidCopyButtonCopied(bool isCopied, string? accountId = null, bool scheduleRefresh = true)
    {
        if (accountId is not null
            && !string.Equals(ViewModel?.SelectedAccount?.Id, accountId, StringComparison.Ordinal))
        {
            return;
        }

        if (CopyUuidButton is null)
            return;

        CopyUuidButton.IsHitTestVisible = !isCopied;
        CopyUuidButton.Cursor = isCopied ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.Hand;
        CopyUuidButton.Foreground = new SolidColorBrush(isCopied
            ? Color.FromRgb(0x7D, 0xFF, 0xB2)
            : Color.FromArgb(0xDF, 0xFF, 0xFF, 0xFF));

        if (CopyUuidCopyIcon is not null)
        {
            CopyUuidCopyIcon.Visibility = isCopied ? Visibility.Collapsed : Visibility.Visible;
            CopyUuidCopyIcon.InvalidateVisual();
        }

        if (CopyUuidPassedIcon is not null)
        {
            CopyUuidPassedIcon.Visibility = isCopied ? Visibility.Visible : Visibility.Collapsed;
            CopyUuidPassedIcon.InvalidateVisual();
        }

        if (isCopied)
        {
            _ = Dispatcher.BeginInvoke(
                () => SetUuidCopyButtonCopied(true, accountId, scheduleRefresh: false),
                System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
    }
}
