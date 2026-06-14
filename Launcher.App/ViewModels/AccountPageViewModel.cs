using Launcher.Application.Accounts;
using Launcher.App.Models;
using Launcher.App.Resources;
using Launcher.App.Services;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels;

public sealed partial class AccountPageViewModel : ObservableObject
{
    private const string DialogBusyIcon = "\uE895";
    private const string DialogSuccessIcon = "\uE73E";
    private const string DialogFailureIcon = "\uE783";
    private const string RenameInputIcon = "\uE70F";
    private static string MicrosoftLoginInitialMessage => Strings.Status_OpeningMicrosoftLogin;
    private static string MicrosoftLoginActiveMessage => Strings.Status_LoginMicrosoftActive;

    private readonly IAccountStore accountStore;
    private readonly IMicrosoftAccountService microsoftAccountService;
    private readonly IStatusService statusService;
    private readonly IAccountDialogService dialogService;
    private readonly IClipboardService clipboardService;
    private readonly IFilePickerService filePickerService;
    private LauncherSettings settings = new();

    [ObservableProperty]
    private LauncherAccount? selectedAccount;

    [ObservableProperty]
    private bool isAddAccountDialogOpen;

    [ObservableProperty]
    private bool isAddAccountDialogBusy;

    [ObservableProperty]
    private AccountTypeOption? selectedAccountTypeOption;

    [ObservableProperty]
    private string addAccountDialogStep = AccountDialogSteps.AddAccountType;

    [ObservableProperty]
    private string newOfflineAccountName = string.Empty;

    [ObservableProperty]
    private bool isNewOfflineAccountNameInvalid;

    [ObservableProperty]
    private string microsoftLoginMessage = MicrosoftLoginInitialMessage;

    [ObservableProperty]
    private string microsoftLoginIcon = DialogBusyIcon;

    [ObservableProperty]
    private bool isMicrosoftLoginSuccessful;

    [ObservableProperty]
    private bool isMicrosoftAccountAlreadyAdded;

    [ObservableProperty]
    private bool isDeleteAccountDialogOpen;

    [ObservableProperty]
    private LauncherAccount? accountPendingDelete;

    [ObservableProperty]
    private AccountCapeOption? selectedAccountCapeOption;

    [ObservableProperty]
    private bool isAccountProfileBusy;

    [ObservableProperty]
    private string accountProfileMessage = string.Empty;

    [ObservableProperty]
    private bool isRenameAccountDialogOpen;

    [ObservableProperty]
    private bool isRenameAccountDialogBusy;

    [ObservableProperty]
    private LauncherAccount? accountPendingRename;

    [ObservableProperty]
    private string renameAccountDialogStep = AccountDialogSteps.RenameInput;

    [ObservableProperty]
    private string renameAccountName = string.Empty;

    [ObservableProperty]
    private bool isRenameAccountNameInvalid;

    [ObservableProperty]
    private bool isRenameAccountSuccessful;

    [ObservableProperty]
    private string renameAccountMessage = string.Empty;

    [ObservableProperty]
    private string renameAccountIcon = RenameInputIcon;

    public AccountPageViewModel(
        IAccountStore accountStore,
        IMicrosoftAccountService microsoftAccountService,
        IStatusService statusService,
        IAccountDialogService dialogService,
        IClipboardService clipboardService,
        IFilePickerService filePickerService)
    {
        this.accountStore = accountStore;
        this.microsoftAccountService = microsoftAccountService;
        this.statusService = statusService;
        this.dialogService = dialogService;
        this.clipboardService = clipboardService;
        this.filePickerService = filePickerService;
    }

    public void NotifyStatusMessage(string message)
    {
        statusService.Report(message);
    }

    public bool IsAccountTypeStep => AddAccountDialogStep == AccountDialogSteps.AddAccountType;
    public bool IsOfflineNameStep => AddAccountDialogStep == AccountDialogSteps.AddAccountOfflineName;
    public bool IsMicrosoftLoginStep => AddAccountDialogStep == AccountDialogSteps.AddAccountMicrosoftLogin;
    public bool IsMicrosoftLoginResultStep => AddAccountDialogStep == AccountDialogSteps.AddAccountMicrosoftResult;
    public bool IsMicrosoftStatusStep => IsMicrosoftLoginStep || IsMicrosoftLoginResultStep;
    public bool CanShowAddAccountBackButton => !IsAddAccountDialogBusy && (IsOfflineNameStep || IsMicrosoftLoginStep);
    public bool CanShowAddAccountCancelButton => !IsAddAccountDialogBusy && !IsMicrosoftLoginResultStep;
    public bool IsAddAccountFooterEnabled => !IsAddAccountDialogBusy;
    public bool CanConfirmAddAccountDialog => !IsAddAccountDialogBusy
        && (IsMicrosoftLoginResultStep || IsOfflineNameStep || (IsAccountTypeStep && SelectedAccountTypeOption is not null));
    public bool IsMicrosoftAccountTypeSelected => IsAccountTypeStep && SelectedAccountTypeOption?.Kind is AccountTypeKinds.Microsoft;
    public bool CanChangeSelectedAccountSkin => SelectedAccount is not null && !SelectedAccount.IsOffline;
    public bool CanEditSelectedMicrosoftAccount => SelectedAccount is not null && !SelectedAccount.IsOffline && !IsAccountProfileBusy;
    public bool CanApplySelectedCape => SelectedAccount is not null && !SelectedAccount.IsOffline && SelectedAccountCapeOption is not null;
    public bool HasSelectedAccountCapes => SelectedAccountCapeOptions.Count > 0;
    public bool IsRenameAccountInputStep => RenameAccountDialogStep == AccountDialogSteps.RenameInput;
    public bool IsRenameAccountStatusStep => RenameAccountDialogStep == AccountDialogSteps.RenameStatus;
    public bool IsRenameAccountResultStep => RenameAccountDialogStep == AccountDialogSteps.RenameResult;
    public bool IsRenameAccountMessageStep => IsRenameAccountStatusStep || IsRenameAccountResultStep;
    public bool IsRenameMicrosoftAccount => AccountPendingRename is not null && !AccountPendingRename.IsOffline;
    public bool CanShowRenameAccountCancelButton => !IsRenameAccountDialogBusy && IsRenameAccountInputStep;
    public bool CanConfirmRenameAccountDialog => !IsRenameAccountDialogBusy
        && (IsRenameAccountResultStep || (IsRenameAccountInputStep && !string.IsNullOrWhiteSpace(RenameAccountName)));
    public string? MicrosoftLoginIconKey => IsMicrosoftLoginStep
        ? "general/general_external-web"
        : IsMicrosoftLoginResultStep
            ? IsMicrosoftLoginSuccessful ? "general/general_passed" : "general/general_attention"
            : null;
    public string? RenameAccountIconKey => IsRenameAccountResultStep
        ? IsRenameAccountSuccessful ? "general/general_passed" : "general/general_attention"
        : null;

    public string RenameAccountDialogTitle =>
        AccountDialogText.GetRenameTitle(RenameAccountDialogStep, IsRenameAccountSuccessful);

    public string RenameAccountDialogSubtitle =>
        AccountDialogText.GetRenameSubtitle(RenameAccountDialogStep, IsRenameMicrosoftAccount);

    public string AddAccountDialogTitle => AccountDialogText.GetAddTitle(
        AddAccountDialogStep,
        IsMicrosoftAccountAlreadyAdded,
        IsMicrosoftLoginSuccessful);

    public string AddAccountDialogSubtitle => AccountDialogText.GetAddSubtitle(AddAccountDialogStep);

    public ObservableCollection<LauncherAccount> Accounts { get; } = [];
    public ObservableCollection<AccountCapeOption> SelectedAccountCapeOptions { get; } = [];
    public ObservableCollection<AccountTypeOption> AccountTypeOptions { get; } = new(AccountTypeOptionFactory.Create());

    public async Task InitializeAsync(LauncherSettings launcherSettings)
    {
        settings = launcherSettings;
        Accounts.Clear();
        foreach (var account in await accountStore.LoadAsync(settings))
            Accounts.Add(account);

        var rememberedAccount = Accounts.FirstOrDefault(account =>
            string.Equals(account.Id, settings.SelectedAccountId, StringComparison.Ordinal));
        if (rememberedAccount is not null)
            SelectAccount(rememberedAccount, persistSelection: false);
        else
            ClearSelectedAccount();
    }

    public void SelectAccount(LauncherAccount account)
    {
        SelectAccount(account, persistSelection: true);
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

    private void SelectAccount(LauncherAccount account, bool persistSelection)
    {
        IsAccountProfileBusy = false;
        SelectedAccount = account;
        foreach (var item in Accounts)
            item.IsSelected = ReferenceEquals(item, account);

        ResetSelectedAccountProfileState(account);
        settings.SelectedAccountId = account.Id;
        if (persistSelection)
            _ = PersistAccountOrderAsync();
    }

    public async Task ChangeSelectedAccountSkinAsync(string skinFilePath)
    {
        var account = SelectedAccount;
        if (account is null)
            return;

        if (account.IsOffline)
        {
            AccountProfileMessage = Strings.Status_SkinOfflineUnsupported;
            return;
        }

        try
        {
            IsAccountProfileBusy = true;
            AccountProfileMessage = Strings.Status_UploadingSkin;
            var updatedAccount = await microsoftAccountService.UploadSkinAsync(account, skinFilePath);
            ReplaceSelectedAccount(account, updatedAccount);
            await PersistAccountOrderAsync();
            AccountProfileMessage = Strings.Status_SkinUpdated;
            await LoadSelectedAccountProfileAsync(updatedAccount);
        }
        catch (Exception)
        {
            AccountProfileMessage = Strings.Status_SkinUpdateFailed;
        }
        finally
        {
            IsAccountProfileBusy = false;
        }
    }

    [RelayCommand]
    public async Task RefreshSelectedAccountProfileAsync()
    {
        if (SelectedAccount is not null)
            await LoadSelectedAccountProfileAsync(SelectedAccount);
    }

    public async Task RefreshCurrentSecondaryContentAsync()
    {
        if (SelectedAccount is null || IsAccountProfileBusy)
            return;

        await RefreshSelectedAccountProfileAsync();
    }

    [RelayCommand]
    public async Task ApplySelectedAccountCapeAsync()
    {
        var account = SelectedAccount;
        var cape = SelectedAccountCapeOption;
        if (account is null || cape is null)
            return;

        if (account.IsOffline)
        {
            AccountProfileMessage = Strings.Status_CapeOfflineUnsupported;
            return;
        }

        try
        {
            IsAccountProfileBusy = true;
            AccountProfileMessage = Strings.Status_ChangingCape;
            await microsoftAccountService.SetActiveCapeAsync(account, cape.Id);
            AccountProfileMessage = cape.IsNone
                ? Strings.Status_CapeRemoved
                : string.Format(Strings.Status_CapeChangedFormat, cape.DisplayName);
            MarkSelectedCapeActive(cape);
            await StoreSelectedAccountCapeCacheAsync();
        }
        catch (Exception)
        {
            AccountProfileMessage = Strings.Status_CapeChangeFailed;
        }
        finally
        {
            IsAccountProfileBusy = false;
        }
    }

    public void OpenAddAccountDialog()
    {
        ResetAddAccountDialogState(clearOfflineName: true);
        IsAddAccountDialogOpen = true;
    }

    public void CancelAddAccountDialog()
    {
        if (IsAddAccountDialogBusy)
            return;

        IsAddAccountDialogOpen = false;
    }

    public void ResetAddAccountDialog()
    {
        ResetAddAccountDialogState(clearOfflineName: true);
    }

    public void BackToAddAccountTypeStep()
    {
        if (IsAddAccountDialogBusy)
            return;

        ResetAddAccountDialogState(clearOfflineName: false);
    }

    public void BeginMicrosoftAccountLogin()
    {
        AddAccountDialogStep = AccountDialogSteps.AddAccountMicrosoftLogin;
        IsAddAccountDialogBusy = true;
        ResetMicrosoftLoginResultState(MicrosoftLoginActiveMessage);
        ReportStatus(Strings.Status_OpeningMicrosoftLogin);
    }

    public async Task CompleteMicrosoftAccountLoginAsync()
    {
        try
        {
            var account = await microsoftAccountService.LoginInteractivelyAsync();
            if (string.IsNullOrWhiteSpace(account.DisplayName) || string.IsNullOrWhiteSpace(account.Uuid))
            {
                var message = Strings.Status_LoginMissingProfile;
                ReportStatus(message);
                ShowMicrosoftLoginResult(false, message);
                return;
            }

            var existing = Accounts.FirstOrDefault(item =>
                !item.IsOffline && string.Equals(item.Uuid, account.Uuid, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                SelectAccount(existing, persistSelection: false);
                await PersistAccountOrderAsync();
                var message = string.Format(Strings.Status_LoginAccountAlreadyAddedFormat, existing.DisplayName);
                ReportStatus(message);
                ShowMicrosoftLoginResult(true, message, alreadyAdded: true);
                return;
            }

            Accounts.Add(account);
            SelectAccount(account, persistSelection: false);
            await PersistAccountOrderAsync();
            var addedMessage = string.Format(Strings.Status_LoginAccountAddedFormat, account.DisplayName);
            ReportStatus(addedMessage);
            ShowMicrosoftLoginResult(true, addedMessage);
        }
        catch (OperationCanceledException)
        {
            var message = Strings.Status_LoginCanceled;
            ReportStatus(message);
            ShowMicrosoftLoginResult(false, message);
        }
        catch (Exception)
        {
            var message = Strings.Status_LoginFailed;
            ReportStatus(message);
            ShowMicrosoftLoginResult(false, message);
        }
        finally
        {
            IsAddAccountDialogBusy = false;
        }
    }

    public void CloseAddAccountDialogAfterMicrosoftResult()
    {
        if (IsAddAccountDialogBusy)
            return;

        IsAddAccountDialogOpen = false;
    }

    public async Task ConfirmAddAccountDialogAsync()
    {
        if (IsAddAccountDialogBusy)
            return;

        if (IsMicrosoftLoginResultStep)
        {
            CloseAddAccountDialogAfterMicrosoftResult();
            return;
        }

        if (SelectedAccountTypeOption is null)
            return;

        if (IsAccountTypeStep)
        {
            if (SelectedAccountTypeOption.Kind is AccountTypeKinds.Offline)
            {
                AddAccountDialogStep = AccountDialogSteps.AddAccountOfflineName;
                return;
            }

            return;
        }

        var accountName = NewOfflineAccountName.Trim();
        if (!IsValidAccountName(accountName))
        {
            IsNewOfflineAccountNameInvalid = true;
            ReportStatus(AccountNameValidator.ValidationMessage);
            return;
        }

        var account = new LauncherAccount
        {
            Id = $"offline-{Guid.NewGuid():N}",
            DisplayName = accountName,
            IsOffline = true
        };

        Accounts.Add(account);
        SelectAccount(account, persistSelection: false);
        await PersistAccountOrderAsync();

        IsAddAccountDialogOpen = false;
        ReportStatus(string.Format(Strings.Status_OfflineAccountAddedFormat, accountName));
    }

    public void OpenDeleteAccountDialog(LauncherAccount account)
    {
        AccountPendingDelete = account;
        IsDeleteAccountDialogOpen = true;
    }

    public void CancelDeleteAccountDialog()
    {
        IsDeleteAccountDialogOpen = false;
        AccountPendingDelete = null;
    }

    public async Task ConfirmDeleteAccountDialogAsync()
    {
        if (AccountPendingDelete is null)
            return;

        var account = AccountPendingDelete;
        var deletedName = account.DisplayName;

        if (ReferenceEquals(SelectedAccount, account))
            ClearSelectedAccount();

        Accounts.Remove(account);
        IsDeleteAccountDialogOpen = false;
        AccountPendingDelete = null;
        ReportStatus(string.Format(Strings.Status_AccountDeletedFormat, deletedName));

        try
        {
            await PersistAccountOrderAsync();
            if (!account.IsOffline)
                await microsoftAccountService.DeleteAccountAsync(account);
        }
        catch (Exception)
        {
            ReportStatus(string.Format(Strings.Status_AccountDeletedCacheCleanupFailedFormat, deletedName));
        }
    }

    public void OpenRenameAccountDialog()
    {
        if (SelectedAccount is null)
            return;

        AccountPendingRename = SelectedAccount;
        ResetRenameAccountDialogState(SelectedAccount.DisplayName);
        IsRenameAccountDialogOpen = true;
    }

    public void CancelRenameAccountDialog()
    {
        if (IsRenameAccountDialogBusy)
            return;

        IsRenameAccountDialogOpen = false;
    }

    public void ResetRenameAccountDialog()
    {
        AccountPendingRename = null;
        ResetRenameAccountDialogState(string.Empty);
    }

    public async Task ConfirmRenameAccountDialogAsync()
    {
        if (IsRenameAccountDialogBusy)
            return;

        if (IsRenameAccountResultStep)
        {
            IsRenameAccountDialogOpen = false;
            return;
        }

        var account = AccountPendingRename;
        if (account is null)
            return;

        var newName = RenameAccountName.Trim();
        if (!IsValidAccountName(newName))
        {
            IsRenameAccountNameInvalid = true;
            ReportStatus(AccountNameValidator.ValidationMessage);
            return;
        }

        if (string.Equals(newName, account.DisplayName, StringComparison.Ordinal))
        {
            ShowRenameAccountResult(true, Strings.Status_AccountNameUnchanged);
            return;
        }

        try
        {
            IsRenameAccountDialogBusy = true;
            RenameAccountDialogStep = AccountDialogSteps.RenameStatus;
            RenameAccountIcon = DialogBusyIcon;
            RenameAccountMessage = account.IsOffline
                ? Strings.Status_SavingOfflineAccountName
                : Strings.Status_ChangingMicrosoftAccountName;

            LauncherAccount updatedAccount;
            if (account.IsOffline)
            {
                updatedAccount = AccountMapper.WithDisplayName(account, newName);
            }
            else
            {
                var renamedAccount = await microsoftAccountService.ChangeNameAsync(account, newName);
                updatedAccount = AccountMapper.WithCapeCache(renamedAccount, account.CachedCapeOptions);
            }

            ReplaceSelectedAccount(account, updatedAccount);
            AccountPendingRename = updatedAccount;
            await PersistAccountOrderAsync();
            var message = string.Format(Strings.Status_AccountRenamedFormat, updatedAccount.DisplayName);
            ReportStatus(message);
            ShowRenameAccountResult(true, string.Format(Strings.Status_AccountRenameResultFormat, updatedAccount.DisplayName));
        }
        catch (Exception)
        {
            ReportStatus(Strings.Status_AccountRenameFailed);
            ShowRenameAccountResult(false, Strings.Status_AccountRenameFailed);
        }
        finally
        {
            IsRenameAccountDialogBusy = false;
        }
    }

    partial void OnAddAccountDialogStepChanged(string value)
    {
        NotifyAddAccountDialogStepPropertiesChanged();
    }

    partial void OnIsAddAccountDialogBusyChanged(bool value)
    {
        NotifyAddAccountDialogActionPropertiesChanged();
    }

    partial void OnIsMicrosoftLoginSuccessfulChanged(bool value)
    {
        OnPropertyChanged(nameof(AddAccountDialogTitle));
        OnPropertyChanged(nameof(MicrosoftLoginIconKey));
    }

    partial void OnIsMicrosoftAccountAlreadyAddedChanged(bool value)
    {
        OnPropertyChanged(nameof(AddAccountDialogTitle));
    }

    partial void OnSelectedAccountTypeOptionChanged(AccountTypeOption? value)
    {
        OnPropertyChanged(nameof(IsMicrosoftAccountTypeSelected));
        OnPropertyChanged(nameof(CanConfirmAddAccountDialog));
    }

    partial void OnSelectedAccountCapeOptionChanged(AccountCapeOption? value)
    {
        OnPropertyChanged(nameof(CanApplySelectedCape));
    }

    partial void OnIsAccountProfileBusyChanged(bool value)
    {
        NotifySelectedAccountProfileActionPropertiesChanged();
    }

    partial void OnSelectedAccountChanged(LauncherAccount? value)
    {
        NotifySelectedAccountCapabilityPropertiesChanged();
    }

    partial void OnRenameAccountDialogStepChanged(string value)
    {
        NotifyRenameAccountDialogStepPropertiesChanged();
    }

    partial void OnIsRenameAccountDialogBusyChanged(bool value)
    {
        NotifyRenameAccountDialogActionPropertiesChanged();
    }

    partial void OnAccountPendingRenameChanged(LauncherAccount? value)
    {
        OnPropertyChanged(nameof(IsRenameMicrosoftAccount));
        OnPropertyChanged(nameof(RenameAccountDialogSubtitle));
    }

    partial void OnIsRenameAccountSuccessfulChanged(bool value)
    {
        OnPropertyChanged(nameof(RenameAccountDialogTitle));
        OnPropertyChanged(nameof(RenameAccountIconKey));
    }

    partial void OnRenameAccountNameChanged(string value)
    {
        IsRenameAccountNameInvalid = false;

        OnPropertyChanged(nameof(CanConfirmRenameAccountDialog));
    }

    partial void OnNewOfflineAccountNameChanged(string value)
    {
        IsNewOfflineAccountNameInvalid = false;
    }

    private void NotifyAddAccountDialogStepPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsAccountTypeStep));
        OnPropertyChanged(nameof(IsOfflineNameStep));
        OnPropertyChanged(nameof(IsMicrosoftLoginStep));
        OnPropertyChanged(nameof(IsMicrosoftLoginResultStep));
        OnPropertyChanged(nameof(IsMicrosoftStatusStep));
        OnPropertyChanged(nameof(MicrosoftLoginIconKey));
        NotifyAddAccountDialogActionPropertiesChanged();
        OnPropertyChanged(nameof(IsMicrosoftAccountTypeSelected));
        OnPropertyChanged(nameof(AddAccountDialogTitle));
        OnPropertyChanged(nameof(AddAccountDialogSubtitle));
    }

    private void NotifyAddAccountDialogActionPropertiesChanged()
    {
        OnPropertyChanged(nameof(CanShowAddAccountBackButton));
        OnPropertyChanged(nameof(CanShowAddAccountCancelButton));
        OnPropertyChanged(nameof(IsAddAccountFooterEnabled));
        OnPropertyChanged(nameof(CanConfirmAddAccountDialog));
    }

    private void NotifyRenameAccountDialogStepPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsRenameAccountInputStep));
        OnPropertyChanged(nameof(IsRenameAccountStatusStep));
        OnPropertyChanged(nameof(IsRenameAccountResultStep));
        OnPropertyChanged(nameof(IsRenameAccountMessageStep));
        OnPropertyChanged(nameof(RenameAccountIconKey));
        NotifyRenameAccountDialogActionPropertiesChanged();
        OnPropertyChanged(nameof(RenameAccountDialogTitle));
        OnPropertyChanged(nameof(RenameAccountDialogSubtitle));
    }

    private void NotifyRenameAccountDialogActionPropertiesChanged()
    {
        OnPropertyChanged(nameof(CanShowRenameAccountCancelButton));
        OnPropertyChanged(nameof(CanConfirmRenameAccountDialog));
    }

    private void NotifySelectedAccountCapabilityPropertiesChanged()
    {
        OnPropertyChanged(nameof(CanChangeSelectedAccountSkin));
        NotifySelectedAccountProfileActionPropertiesChanged();
    }

    private void NotifySelectedAccountProfileActionPropertiesChanged()
    {
        OnPropertyChanged(nameof(CanEditSelectedMicrosoftAccount));
        OnPropertyChanged(nameof(CanApplySelectedCape));
    }

    private async Task PersistAccountOrderAsync()
    {
        settings.SelectedAccountId = SelectedAccount?.Id;
        await accountStore.SaveOrderAsync(settings, Accounts);
    }

    private async Task LoadSelectedAccountProfileAsync(LauncherAccount account)
    {
        SelectedAccountCapeOptions.Clear();
        SelectedAccountCapeOption = null;
        OnPropertyChanged(nameof(HasSelectedAccountCapes));
        OnPropertyChanged(nameof(CanApplySelectedCape));

        if (!ReferenceEquals(SelectedAccount, account))
            return;

        if (account.IsOffline)
        {
            AccountProfileMessage = Strings.Account_ProfileOfflineUnsupported;
            return;
        }

        try
        {
            IsAccountProfileBusy = true;
            AccountProfileMessage = Strings.Status_LoadingAccountProfile;
            var capes = await microsoftAccountService.GetCapesAsync(account);
            if (!ReferenceEquals(SelectedAccount, account))
                return;

            PopulateSelectedAccountCapeOptions(capes);
            await StoreSelectedAccountCapeCacheAsync();
            OnPropertyChanged(nameof(CanApplySelectedCape));
            AccountProfileMessage = SelectedAccountCapeOptions.Count == 0
                ? Strings.Account_ProfileNoCapes
                : Strings.Account_ProfileLoaded;
        }
        catch (Exception)
        {
            if (ReferenceEquals(SelectedAccount, account))
                AccountProfileMessage = Strings.Status_LoadAccountProfileFailed;
        }
        finally
        {
            if (ReferenceEquals(SelectedAccount, account))
                IsAccountProfileBusy = false;
        }
    }

    private void ResetSelectedAccountProfileState(LauncherAccount account)
    {
        PopulateSelectedAccountCapeOptions(account.CachedCapeOptions);
        AccountProfileMessage = account.IsOffline
            ? Strings.Account_ProfileOfflineUnsupported
            : SelectedAccountCapeOptions.Count > 0
                ? Strings.Account_ProfileCacheLoaded
                : Strings.Account_ProfileRefreshHint;
    }

    private void PopulateSelectedAccountCapeOptions(IEnumerable<AccountCapeOption> capes)
    {
        SelectedAccountCapeOptions.Clear();
        foreach (var cape in capes)
            SelectedAccountCapeOptions.Add(cape);

        SelectedAccountCapeOption = SelectedAccountCapeOptions.FirstOrDefault(cape => cape.IsActive)
            ?? SelectedAccountCapeOptions.FirstOrDefault();
        OnPropertyChanged(nameof(HasSelectedAccountCapes));
        OnPropertyChanged(nameof(CanApplySelectedCape));
    }

    private void MarkSelectedCapeActive(AccountCapeOption activeCape)
    {
        var updatedCapes = SelectedAccountCapeOptions
            .Select(cape => new AccountCapeOption
            {
                Id = cape.Id,
                DisplayName = cape.DisplayName,
                ImageUrl = cape.ImageUrl,
                IsNone = cape.IsNone,
                IsActive = cape.IsNone == activeCape.IsNone
                    && string.Equals(cape.Id, activeCape.Id, StringComparison.OrdinalIgnoreCase)
            })
            .ToList();

        PopulateSelectedAccountCapeOptions(updatedCapes);
    }

    private async Task StoreSelectedAccountCapeCacheAsync()
    {
        var account = SelectedAccount;
        if (account is null)
            return;

        ReplaceSelectedAccount(account, AccountMapper.WithCapeCache(account, SelectedAccountCapeOptions.ToList()));
        await PersistAccountOrderAsync();
    }

    private void ReplaceSelectedAccount(LauncherAccount oldAccount, LauncherAccount newAccount)
    {
        var index = Accounts.IndexOf(oldAccount);
        if (index >= 0)
            Accounts[index] = newAccount;

        SelectedAccount = newAccount;
        foreach (var item in Accounts)
            item.IsSelected = ReferenceEquals(item, newAccount);

        settings.SelectedAccountId = newAccount.Id;
        NotifySelectedAccountCapabilityPropertiesChanged();
    }

    private void ClearSelectedAccount()
    {
        SelectedAccount = null;
        settings.SelectedAccountId = null;
        foreach (var item in Accounts)
            item.IsSelected = false;
    }

    private void ResetAddAccountDialogState(bool clearOfflineName)
    {
        AddAccountDialogStep = AccountDialogSteps.AddAccountType;
        if (clearOfflineName)
            NewOfflineAccountName = string.Empty;

        IsNewOfflineAccountNameInvalid = false;
        IsAddAccountDialogBusy = false;
        ResetMicrosoftLoginResultState();
        SelectedAccountTypeOption = null;
    }

    private void ResetMicrosoftLoginResultState(string? message = null)
    {
        IsMicrosoftLoginSuccessful = false;
        IsMicrosoftAccountAlreadyAdded = false;
        MicrosoftLoginIcon = DialogBusyIcon;
        MicrosoftLoginMessage = message ?? MicrosoftLoginInitialMessage;
    }

    private void ResetRenameAccountDialogState(string accountName)
    {
        IsRenameAccountDialogBusy = false;
        RenameAccountDialogStep = AccountDialogSteps.RenameInput;
        RenameAccountName = accountName;
        IsRenameAccountNameInvalid = false;
        IsRenameAccountSuccessful = false;
        RenameAccountIcon = RenameInputIcon;
        RenameAccountMessage = string.Empty;
    }

    private void ShowMicrosoftLoginResult(bool isSuccess, string message, bool alreadyAdded = false)
    {
        IsMicrosoftLoginSuccessful = isSuccess;
        IsMicrosoftAccountAlreadyAdded = alreadyAdded;
        MicrosoftLoginIcon = isSuccess ? DialogSuccessIcon : DialogFailureIcon;
        MicrosoftLoginMessage = message;
        AddAccountDialogStep = AccountDialogSteps.AddAccountMicrosoftResult;
    }

    private void ShowRenameAccountResult(bool isSuccess, string message)
    {
        IsRenameAccountSuccessful = isSuccess;
        RenameAccountIcon = isSuccess ? DialogSuccessIcon : DialogFailureIcon;
        RenameAccountMessage = message;
        RenameAccountDialogStep = AccountDialogSteps.RenameResult;
    }

    private static bool IsValidAccountName(string accountName)
    {
        return AccountNameValidator.IsValid(accountName);
    }

    private void ReportStatus(string message)
    {
        statusService.Report(message);
    }

}
