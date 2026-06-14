using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Application.Accounts;
using Launcher.App.Models;
using Launcher.App.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels;

public sealed partial class AccountPageViewModel : ObservableObject
{
    private readonly IStatusService statusService;
    private readonly IAccountDialogService dialogService;
    private readonly IClipboardService clipboardService;
    private readonly IFilePickerService filePickerService;

    public AccountPageViewModel(
        AccountListViewModel accountList,
        AccountDialogViewModel dialog,
        AccountAppearanceViewModel appearance,
        IStatusService statusService,
        IAccountDialogService dialogService,
        IClipboardService clipboardService,
        IFilePickerService filePickerService)
    {
        AccountList = accountList;
        Dialog = dialog;
        Appearance = appearance;
        this.statusService = statusService;
        this.dialogService = dialogService;
        this.clipboardService = clipboardService;
        this.filePickerService = filePickerService;

        AccountList.PropertyChanged += ForwardChildPropertyChanged;
        Dialog.PropertyChanged += ForwardChildPropertyChanged;
        Appearance.PropertyChanged += ForwardChildPropertyChanged;
    }

    public AccountListViewModel AccountList { get; }

    public AccountDialogViewModel Dialog { get; }

    public AccountAppearanceViewModel Appearance { get; }

    public ObservableCollection<LauncherAccount> Accounts => AccountList.Accounts;
    public ObservableCollection<AccountCapeOption> SelectedAccountCapeOptions => Appearance.SelectedAccountCapeOptions;
    public ObservableCollection<AccountTypeOption> AccountTypeOptions => Dialog.AccountTypeOptions;

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

    public bool IsAddAccountDialogOpen
    {
        get => Dialog.IsAddAccountDialogOpen;
        set => Dialog.IsAddAccountDialogOpen = value;
    }

    public bool IsAddAccountDialogBusy
    {
        get => Dialog.IsAddAccountDialogBusy;
        set => Dialog.IsAddAccountDialogBusy = value;
    }

    public AccountTypeOption? SelectedAccountTypeOption
    {
        get => Dialog.SelectedAccountTypeOption;
        set => Dialog.SelectedAccountTypeOption = value;
    }

    public string AddAccountDialogStep
    {
        get => Dialog.AddAccountDialogStep;
        set => Dialog.AddAccountDialogStep = value;
    }

    public string NewOfflineAccountName
    {
        get => Dialog.NewOfflineAccountName;
        set => Dialog.NewOfflineAccountName = value;
    }

    public bool IsNewOfflineAccountNameInvalid
    {
        get => Dialog.IsNewOfflineAccountNameInvalid;
        set => Dialog.IsNewOfflineAccountNameInvalid = value;
    }

    public string MicrosoftLoginMessage
    {
        get => Dialog.MicrosoftLoginMessage;
        set => Dialog.MicrosoftLoginMessage = value;
    }

    public string MicrosoftLoginIcon
    {
        get => Dialog.MicrosoftLoginIcon;
        set => Dialog.MicrosoftLoginIcon = value;
    }

    public bool IsMicrosoftLoginSuccessful
    {
        get => Dialog.IsMicrosoftLoginSuccessful;
        set => Dialog.IsMicrosoftLoginSuccessful = value;
    }

    public bool IsMicrosoftAccountAlreadyAdded
    {
        get => Dialog.IsMicrosoftAccountAlreadyAdded;
        set => Dialog.IsMicrosoftAccountAlreadyAdded = value;
    }

    public bool IsDeleteAccountDialogOpen
    {
        get => Dialog.IsDeleteAccountDialogOpen;
        set => Dialog.IsDeleteAccountDialogOpen = value;
    }

    public LauncherAccount? AccountPendingDelete
    {
        get => Dialog.AccountPendingDelete;
        set => Dialog.AccountPendingDelete = value;
    }

    public AccountCapeOption? SelectedAccountCapeOption
    {
        get => Appearance.SelectedAccountCapeOption;
        set => Appearance.SelectedAccountCapeOption = value;
    }

    public bool IsAccountProfileBusy
    {
        get => Appearance.IsAccountProfileBusy;
        set => Appearance.IsAccountProfileBusy = value;
    }

    public string AccountProfileMessage
    {
        get => Appearance.AccountProfileMessage;
        set => Appearance.AccountProfileMessage = value;
    }

    public bool IsRenameAccountDialogOpen
    {
        get => Dialog.IsRenameAccountDialogOpen;
        set => Dialog.IsRenameAccountDialogOpen = value;
    }

    public bool IsRenameAccountDialogBusy
    {
        get => Dialog.IsRenameAccountDialogBusy;
        set => Dialog.IsRenameAccountDialogBusy = value;
    }

    public LauncherAccount? AccountPendingRename
    {
        get => Dialog.AccountPendingRename;
        set => Dialog.AccountPendingRename = value;
    }

    public string RenameAccountDialogStep
    {
        get => Dialog.RenameAccountDialogStep;
        set => Dialog.RenameAccountDialogStep = value;
    }

    public string RenameAccountName
    {
        get => Dialog.RenameAccountName;
        set => Dialog.RenameAccountName = value;
    }

    public bool IsRenameAccountNameInvalid
    {
        get => Dialog.IsRenameAccountNameInvalid;
        set => Dialog.IsRenameAccountNameInvalid = value;
    }

    public bool IsRenameAccountSuccessful
    {
        get => Dialog.IsRenameAccountSuccessful;
        set => Dialog.IsRenameAccountSuccessful = value;
    }

    public string RenameAccountMessage
    {
        get => Dialog.RenameAccountMessage;
        set => Dialog.RenameAccountMessage = value;
    }

    public string RenameAccountIcon
    {
        get => Dialog.RenameAccountIcon;
        set => Dialog.RenameAccountIcon = value;
    }

    public bool IsAccountTypeStep => Dialog.IsAccountTypeStep;
    public bool IsOfflineNameStep => Dialog.IsOfflineNameStep;
    public bool IsMicrosoftLoginStep => Dialog.IsMicrosoftLoginStep;
    public bool IsMicrosoftLoginResultStep => Dialog.IsMicrosoftLoginResultStep;
    public bool IsMicrosoftStatusStep => Dialog.IsMicrosoftStatusStep;
    public bool CanShowAddAccountBackButton => Dialog.CanShowAddAccountBackButton;
    public bool CanShowAddAccountCancelButton => Dialog.CanShowAddAccountCancelButton;
    public bool IsAddAccountFooterEnabled => Dialog.IsAddAccountFooterEnabled;
    public bool CanConfirmAddAccountDialog => Dialog.CanConfirmAddAccountDialog;
    public bool IsMicrosoftAccountTypeSelected => Dialog.IsMicrosoftAccountTypeSelected;
    public bool CanChangeSelectedAccountSkin => Appearance.CanChangeSelectedAccountSkin;
    public bool CanEditSelectedMicrosoftAccount => Appearance.CanEditSelectedMicrosoftAccount;
    public bool CanApplySelectedCape => Appearance.CanApplySelectedCape;
    public bool HasSelectedAccountCapes => Appearance.HasSelectedAccountCapes;
    public bool IsRenameAccountInputStep => Dialog.IsRenameAccountInputStep;
    public bool IsRenameAccountStatusStep => Dialog.IsRenameAccountStatusStep;
    public bool IsRenameAccountResultStep => Dialog.IsRenameAccountResultStep;
    public bool IsRenameAccountMessageStep => Dialog.IsRenameAccountMessageStep;
    public bool IsRenameMicrosoftAccount => Dialog.IsRenameMicrosoftAccount;
    public bool CanShowRenameAccountCancelButton => Dialog.CanShowRenameAccountCancelButton;
    public bool CanConfirmRenameAccountDialog => Dialog.CanConfirmRenameAccountDialog;
    public string? MicrosoftLoginIconKey => Dialog.MicrosoftLoginIconKey;
    public string? RenameAccountIconKey => Dialog.RenameAccountIconKey;
    public string RenameAccountDialogTitle => Dialog.RenameAccountDialogTitle;
    public string RenameAccountDialogSubtitle => Dialog.RenameAccountDialogSubtitle;
    public string AddAccountDialogTitle => Dialog.AddAccountDialogTitle;
    public string AddAccountDialogSubtitle => Dialog.AddAccountDialogSubtitle;

    public Task InitializeAsync(LauncherSettings launcherSettings)
    {
        return AccountList.InitializeAsync(launcherSettings);
    }

    public void NotifyStatusMessage(string message)
    {
        statusService.Report(message);
    }

    public void SelectAccount(LauncherAccount account)
    {
        AccountList.SelectAccount(account);
    }

    public Task ChangeSelectedAccountSkinAsync(string skinFilePath)
    {
        return Appearance.ChangeSelectedAccountSkinAsync(skinFilePath);
    }

    public Task RefreshCurrentSecondaryContentAsync()
    {
        return Appearance.RefreshCurrentSecondaryContentAsync();
    }

    public void OpenAddAccountDialog()
    {
        Dialog.OpenAddAccountDialog();
    }

    public void CancelAddAccountDialog()
    {
        Dialog.CancelAddAccountDialog();
    }

    public void ResetAddAccountDialog()
    {
        Dialog.ResetAddAccountDialog();
    }

    public void BackToAddAccountTypeStep()
    {
        Dialog.BackToAddAccountTypeStep();
    }

    public void BeginMicrosoftAccountLogin()
    {
        Dialog.BeginMicrosoftAccountLogin();
    }

    public Task CompleteMicrosoftAccountLoginAsync()
    {
        return Dialog.CompleteMicrosoftAccountLoginAsync();
    }

    public void CloseAddAccountDialogAfterMicrosoftResult()
    {
        Dialog.CloseAddAccountDialogAfterMicrosoftResult();
    }

    public Task ConfirmAddAccountDialogAsync()
    {
        return Dialog.ConfirmAddAccountDialogAsync();
    }

    public void OpenDeleteAccountDialog(LauncherAccount account)
    {
        Dialog.OpenDeleteAccountDialog(account);
    }

    public void CancelDeleteAccountDialog()
    {
        Dialog.CancelDeleteAccountDialog();
    }

    public Task ConfirmDeleteAccountDialogAsync()
    {
        return Dialog.ConfirmDeleteAccountDialogAsync();
    }

    public void OpenRenameAccountDialog()
    {
        Dialog.OpenRenameAccountDialog();
    }

    public void CancelRenameAccountDialog()
    {
        Dialog.CancelRenameAccountDialog();
    }

    public void ResetRenameAccountDialog()
    {
        Dialog.ResetRenameAccountDialog();
    }

    public Task ConfirmRenameAccountDialogAsync()
    {
        return Dialog.ConfirmRenameAccountDialogAsync();
    }

    [RelayCommand]
    private void SelectAccountItem(LauncherAccount account)
    {
        SelectAccount(account);
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
    private void CopySelectedUuid()
    {
        var uuid = SelectedAccount?.Uuid;
        if (!string.IsNullOrWhiteSpace(uuid))
            clipboardService.CopyText(uuid);
    }

    [RelayCommand]
    private async Task PickAndChangeSelectedAccountSkinAsync()
    {
        if (!CanChangeSelectedAccountSkin)
            return;

        var skinFilePath = filePickerService.PickMinecraftSkin();
        if (!string.IsNullOrWhiteSpace(skinFilePath))
            await ChangeSelectedAccountSkinAsync(skinFilePath);
    }

    [RelayCommand]
    public Task RefreshSelectedAccountProfileAsync()
    {
        return Appearance.RefreshSelectedAccountProfileAsync();
    }

    [RelayCommand]
    public Task ApplySelectedAccountCapeAsync()
    {
        return Appearance.ApplySelectedAccountCapeAsync();
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

    private void ForwardChildPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.PropertyName))
            OnPropertyChanged(e.PropertyName);
    }
}
