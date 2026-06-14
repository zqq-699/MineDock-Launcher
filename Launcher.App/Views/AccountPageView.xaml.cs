using System.Windows;
using System.Windows.Controls;
using System.Threading;
using Launcher.App.Models;
using Launcher.App.ViewModels;
using Microsoft.Win32;

namespace Launcher.App.Views;

public partial class AccountPageView : UserControl
{
    private const int ClipboardRetryCount = 8;
    private const int ClipboardRetryDelayMilliseconds = 35;

    public AccountPageView()
    {
        InitializeComponent();
    }

    public event Action? AddAccountRequested;
    public event Action<LauncherAccount>? DeleteAccountRequested;
    public event Action? RenameAccountRequested;

    public FrameworkElement RootElement => PageRoot;

    private AccountPageViewModel? ViewModel => DataContext as AccountPageViewModel;

    private void Account_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LauncherAccount account })
            ViewModel?.SelectAccount(account);
    }

    private void EditAccountName_Click(object sender, RoutedEventArgs e)
    {
        RenameAccountRequested?.Invoke();
    }

    private void CopyUuid_Click(object sender, RoutedEventArgs e)
    {
        var uuid = ViewModel?.SelectedAccount?.Uuid;
        if (string.IsNullOrWhiteSpace(uuid))
            return;

        CopyTextToClipboardInBackground(uuid);
    }

    private void CopyTextToClipboardInBackground(string text)
    {
        var thread = new Thread(() =>
        {
            TrySetClipboardText(text);
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
    }

    private static void TrySetClipboardText(string text)
    {
        for (var attempt = 0; attempt < ClipboardRetryCount; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch
            {
                Thread.Sleep(ClipboardRetryDelayMilliseconds * (attempt + 1));
            }
        }
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
}
