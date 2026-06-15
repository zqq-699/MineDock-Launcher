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
    private readonly IMinecraftSkinFileValidator skinFileValidator;

    public AccountPageViewModel(
        AccountListViewModel accountList,
        AccountDialogViewModel dialog,
        AccountAppearanceViewModel appearance,
        AccountOfflineUuidViewModel offlineUuid,
        AccountSkinModelDialogViewModel skinModelDialog,
        IStatusService statusService,
        IAccountDialogService dialogService,
        IClipboardService clipboardService,
        IFilePickerService filePickerService,
        IMinecraftSkinFileValidator skinFileValidator)
    {
        AccountList = accountList;
        Dialog = dialog;
        Appearance = appearance;
        OfflineUuid = offlineUuid;
        SkinModelDialog = skinModelDialog;
        this.statusService = statusService;
        this.dialogService = dialogService;
        this.clipboardService = clipboardService;
        this.filePickerService = filePickerService;
        this.skinFileValidator = skinFileValidator;

        AccountList.PropertyChanged += ForwardChildPropertyChanged;
        Dialog.PropertyChanged += ForwardChildPropertyChanged;
        Appearance.PropertyChanged += ForwardChildPropertyChanged;
        OfflineUuid.PropertyChanged += ForwardChildPropertyChanged;
        SkinModelDialog.PropertyChanged += ForwardChildPropertyChanged;
    }

    public AccountListViewModel AccountList { get; }

    public AccountDialogViewModel Dialog { get; }

    public AccountAppearanceViewModel Appearance { get; }

    public AccountOfflineUuidViewModel OfflineUuid { get; }

    public AccountSkinModelDialogViewModel SkinModelDialog { get; }

    public ObservableCollection<LauncherAccount> Accounts => AccountList.Accounts;
    public ObservableCollection<AccountCapeOption> SelectedAccountCapeOptions => Appearance.SelectedAccountCapeOptions;
    public ObservableCollection<AccountTypeOption> AccountTypeOptions => Dialog.AccountTypeOptions;
    public ObservableCollection<AccountSkinModelOption> SkinModelOptions => SkinModelDialog.SkinModelOptions;

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

    public string AccountProfileErrorCodeMessage
    {
        get => Appearance.AccountProfileErrorCodeMessage;
        set => Appearance.AccountProfileErrorCodeMessage = value;
    }

    public bool IsRenameAccountDialogOpen
    {
        get => Dialog.IsRenameAccountDialogOpen;
        set => Dialog.IsRenameAccountDialogOpen = value;
    }

    public bool IsSkinModelDialogOpen
    {
        get => SkinModelDialog.IsSkinModelDialogOpen;
        set => SkinModelDialog.IsSkinModelDialogOpen = value;
    }

    public AccountSkinModelOption? SelectedSkinModelOption
    {
        get => SkinModelDialog.SelectedSkinModelOption;
        set => SkinModelDialog.SelectedSkinModelOption = value;
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

    public string RenameAccountErrorCodeMessage
    {
        get => Dialog.RenameAccountErrorCodeMessage;
        set => Dialog.RenameAccountErrorCodeMessage = value;
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
    public bool HasAccountProfileErrorCode => Appearance.HasAccountProfileErrorCode;
    public bool IsRenameAccountInputStep => Dialog.IsRenameAccountInputStep;
    public bool IsRenameAccountStatusStep => Dialog.IsRenameAccountStatusStep;
    public bool IsRenameAccountResultStep => Dialog.IsRenameAccountResultStep;
    public bool IsRenameAccountMessageStep => Dialog.IsRenameAccountMessageStep;
    public bool IsRenameMicrosoftAccount => Dialog.IsRenameMicrosoftAccount;
    public bool CanShowRenameAccountCancelButton => Dialog.CanShowRenameAccountCancelButton;
    public bool CanConfirmRenameAccountDialog => Dialog.CanConfirmRenameAccountDialog;
    public bool HasRenameAccountErrorCode => Dialog.HasRenameAccountErrorCode;
    public bool CanConfirmSkinModelDialog => SkinModelDialog.CanConfirmSkinModelDialog;
    public bool IsSkinModelSelectionStep => SkinModelDialog.IsSkinModelSelectionStep;
    public bool CanShowSkinModelDialogCancelButton => SkinModelDialog.CanShowSkinModelDialogCancelButton;
    public string SkinModelDialogTitle => SkinModelDialog.SkinModelDialogTitle;
    public string SkinModelDialogSubtitle => SkinModelDialog.SkinModelDialogSubtitle;
    public string? MicrosoftLoginIconKey => Dialog.MicrosoftLoginIconKey;
    public string? RenameAccountIconKey => Dialog.RenameAccountIconKey;
    public string RenameAccountDialogTitle => Dialog.RenameAccountDialogTitle;
    public string RenameAccountDialogSubtitle => Dialog.RenameAccountDialogSubtitle;
    public string AddAccountDialogTitle => Dialog.AddAccountDialogTitle;
    public string AddAccountDialogSubtitle => Dialog.AddAccountDialogSubtitle;

    public async Task InitializeAsync(LauncherSettings launcherSettings)
    {
        await AccountList.InitializeAsync(launcherSettings);
        _ = Appearance.RefreshMicrosoftAccountsSilentlyAsync();
    }

    public void PrimeFromSettings(LauncherSettings launcherSettings)
    {
        AccountList.PrimeFromSettings(launcherSettings);
    }

    public void NotifyStatusMessage(string message)
    {
        statusService.Report(message);
    }

    public void SelectAccount(LauncherAccount account)
    {
        AccountList.SelectAccount(account);
    }

    public Task ChangeSelectedAccountSkinAsync(string skinFilePath, MinecraftSkinModel skinModel)
    {
        return Appearance.ChangeSelectedAccountSkinAsync(skinFilePath, skinModel);
    }

    public Task RefreshCurrentSecondaryContentAsync()
    {
        return Appearance.RefreshCurrentSecondaryContentAsync();
    }

    [RelayCommand]
    public Task RefreshSelectedAccountInfoAsync()
    {
        return Appearance.RefreshSelectedAccountInfoAsync();
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

    public void OpenSkinModelDialog(string skinFilePath)
    {
        SkinModelDialog.Open(skinFilePath);
    }

    public void OpenSkinFormatErrorDialog()
    {
        SkinModelDialog.OpenFormatError();
    }

    public void CancelSkinModelDialog()
    {
        SkinModelDialog.Cancel();
    }

    public void ResetSkinModelDialog()
    {
        SkinModelDialog.Reset();
    }

    public async Task ConfirmSkinModelDialogAsync()
    {
        if (SkinModelDialog.IsSkinFormatError)
        {
            SkinModelDialog.Cancel();
            return;
        }

        if (!SkinModelDialog.TryConsumeSelection(out var skinFilePath, out var skinModel))
            return;

        await ChangeSelectedAccountSkinAsync(skinFilePath, skinModel);
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
        {
            var validation = await skinFileValidator.ValidateAsync(skinFilePath);
            if (validation.IsValid)
                dialogService.ShowSkinModelDialog(skinFilePath);
            else
                dialogService.ShowSkinFormatErrorDialog();
        }
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

    [RelayCommand]
    private void RequestCancelSkinModelDialog()
    {
        dialogService.CancelSkinModelDialog();
    }

    [RelayCommand]
    private Task RequestConfirmSkinModelDialogAsync()
    {
        return dialogService.ConfirmSkinModelDialogAsync();
    }

    private void ForwardChildPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.PropertyName))
            OnPropertyChanged(e.PropertyName);
    }
}
