using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Application.Accounts;
using Launcher.App.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels;

public sealed partial class AccountPageViewModel : ObservableObject
{
    private readonly IAccountDialogService dialogService;

    public AccountPageViewModel(
        AccountListViewModel accountList,
        AccountDialogViewModel dialog,
        AccountAppearanceViewModel appearance,
        AccountOfflineUuidViewModel offlineUuid,
        IAccountDialogService dialogService)
    {
        AccountList = accountList;
        Dialog = dialog;
        Appearance = appearance;
        OfflineUuid = offlineUuid;
        this.dialogService = dialogService;

        AccountList.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AccountListViewModel.SelectedAccount))
                OnPropertyChanged(nameof(SelectedAccount));
        };
    }

    public AccountListViewModel AccountList { get; }

    public AccountDialogViewModel Dialog { get; }

    public AccountAppearanceViewModel Appearance { get; }

    public AccountOfflineUuidViewModel OfflineUuid { get; }

    public LauncherAccount? SelectedAccount
    {
        get => AccountList.SelectedAccount;
        set
        {
            if (value is null)
                AccountList.ClearSelectedAccount();
            else
                AccountList.SelectAccount(value);
        }
    }

    public async Task InitializeAsync(LauncherSettings launcherSettings)
    {
        await AccountList.InitializeAsync(launcherSettings);
        _ = Appearance.RefreshMicrosoftAccountsSilentlyAsync();
    }

    public void PrimeFromSettings(LauncherSettings launcherSettings)
    {
        AccountList.PrimeFromSettings(launcherSettings);
    }

    public void SelectAccount(LauncherAccount account)
    {
        AccountList.SelectAccount(account);
    }

    [RelayCommand]
    private void RequestAddAccount()
    {
        dialogService.ShowAddAccountDialog();
    }

    [RelayCommand]
    private void RequestDeleteAccount(LauncherAccount account)
    {
        dialogService.ShowDeleteAccountDialog(account);
    }

    [RelayCommand]
    private void RequestRenameAccount()
    {
        dialogService.ShowRenameAccountDialog();
    }

    [RelayCommand]
    private void RequestCancelAddAccountDialog()
    {
        dialogService.CancelAddAccountDialog();
    }

    [RelayCommand]
    private void RequestBackAddAccountDialog()
    {
        dialogService.BackAddAccountDialog();
    }

    [RelayCommand]
    private Task RequestConfirmAddAccountDialogAsync()
    {
        return dialogService.ConfirmAddAccountDialogAsync();
    }

    [RelayCommand]
    private void RequestCancelDeleteAccountDialog()
    {
        dialogService.CancelDeleteAccountDialog();
    }

    [RelayCommand]
    private Task RequestConfirmDeleteAccountDialogAsync()
    {
        return dialogService.ConfirmDeleteAccountDialogAsync();
    }

    [RelayCommand]
    private void RequestCancelRenameAccountDialog()
    {
        dialogService.CancelRenameAccountDialog();
    }

    [RelayCommand]
    private Task RequestConfirmRenameAccountDialogAsync()
    {
        return dialogService.ConfirmRenameAccountDialogAsync();
    }
}
