using Launcher.Application.Accounts;
using Launcher.App.Models;
using Launcher.App.Services;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
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
    private const string MicrosoftLoginInitialMessage = "\u6b63\u5728\u6253\u5f00 Microsoft \u5b98\u65b9\u767b\u5f55\u9875\u9762...";
    private const string MicrosoftLoginActiveMessage = "\u6b63\u5728\u767b\u5f55\uff0c\u8bf7\u5728\u5f39\u51fa\u7684 Microsoft \u5b98\u65b9\u9875\u9762\u5b8c\u6210\u767b\u5f55...";
    private const string AccountNameValidationMessage = "\u7528\u6237\u540d\u9700\u4e3a 3-16 \u4f4d\u5b57\u6bcd\u3001\u6570\u5b57\u6216\u4e0b\u5212\u7ebf";

    private static readonly Regex AccountNameRegex = new("^[A-Za-z0-9_]{3,16}$", RegexOptions.CultureInvariant);

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

    public string RenameAccountDialogTitle => RenameAccountDialogStep switch
    {
        AccountDialogSteps.RenameStatus => "\u6b63\u5728\u4fee\u6539",
        AccountDialogSteps.RenameResult => IsRenameAccountSuccessful ? "\u4fee\u6539\u6210\u529f" : "\u4fee\u6539\u5931\u8d25",
        _ => "\u4fee\u6539\u8d26\u6237\u540d"
    };

    public string RenameAccountDialogSubtitle => RenameAccountDialogStep switch
    {
        AccountDialogSteps.RenameStatus => "\u8bf7\u7a0d\u7b49\uff0c\u6b63\u5728\u5904\u7406\u8d26\u6237\u540d\u4fee\u6539\u3002",
        AccountDialogSteps.RenameResult => "\u70b9\u51fb\u786e\u5b9a\u8fd4\u56de\u8d26\u6237\u8be6\u60c5\u3002",
        _ => IsRenameMicrosoftAccount
            ? "\u6b63\u7248\u8d26\u6237\u6bcf 30 \u5929\u53ef\u6539\u4e00\u6b21\u540d\uff0c\u8c28\u614e\u64cd\u4f5c\u3002"
            : "\u8f93\u5165\u65b0\u7684\u79bb\u7ebf\u8d26\u6237\u540d\u3002"
    };

    public string AddAccountDialogTitle => AddAccountDialogStep switch
    {
        AccountDialogSteps.AddAccountOfflineName => "\u79bb\u7ebf\u8d26\u6237",
        AccountDialogSteps.AddAccountMicrosoftLogin => "\u6b63\u7248\u767b\u5f55",
        AccountDialogSteps.AddAccountMicrosoftResult => IsMicrosoftAccountAlreadyAdded
            ? "\u8d26\u53f7\u5df2\u5b58\u5728"
            : IsMicrosoftLoginSuccessful ? "\u767b\u5f55\u6210\u529f" : "\u767b\u5f55\u672a\u5b8c\u6210",
        _ => "\u6dfb\u52a0\u8d26\u6237"
    };

    public string AddAccountDialogSubtitle => IsOfflineNameStep
        ? "\u8f93\u5165\u8981\u6dfb\u52a0\u7684\u79bb\u7ebf\u8d26\u6237\u540d\u3002"
        : IsMicrosoftLoginStep
        ? "\u8bf7\u5728\u5f39\u51fa\u7684 Microsoft \u5b98\u65b9\u9875\u9762\u5b8c\u6210\u767b\u5f55\u3002"
        : IsMicrosoftLoginResultStep
        ? "\u70b9\u51fb\u786e\u5b9a\u8fd4\u56de\u8d26\u6237\u5217\u8868\u3002"
        : "\u9009\u62e9\u8981\u6dfb\u52a0\u7684\u8d26\u6237\u7c7b\u578b\u3002";

    public ObservableCollection<LauncherAccount> Accounts { get; } = [];
    public ObservableCollection<AccountCapeOption> SelectedAccountCapeOptions { get; } = [];
    public ObservableCollection<AccountTypeOption> AccountTypeOptions { get; } =
    [
        new()
        {
            Kind = AccountTypeKinds.Offline,
            Title = "\u79bb\u7ebf\u8d26\u6237",
            Description = "\u4f7f\u7528\u672c\u5730\u7528\u6237\u540d\u8fdb\u5165\u6e38\u620f\u3002",
            Icon = "\uE77B",
            IconKey = "account_page/account_page_add_account_dialog_offline_user"
        },
        new()
        {
            Kind = AccountTypeKinds.Microsoft,
            Title = "\u6b63\u7248\u8d26\u6237",
            Description = "\u901a\u8fc7 Microsoft \u8d26\u6237\u767b\u5f55\u5e76\u83b7\u53d6\u76ae\u80a4\u5934\u50cf\u3002",
            Icon = "\uE72E",
            IconKey = "account_page/account_page_add_account_dialog_online_user"
        }
    ];

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
            AccountProfileMessage = "\u53ea\u6709\u6b63\u7248\u8d26\u6237\u53ef\u4ee5\u66f4\u6362\u76ae\u80a4";
            return;
        }

        try
        {
            IsAccountProfileBusy = true;
            AccountProfileMessage = "\u6b63\u5728\u4e0a\u4f20\u76ae\u80a4...";
            var updatedAccount = await microsoftAccountService.UploadSkinAsync(account, skinFilePath);
            ReplaceSelectedAccount(account, updatedAccount);
            await PersistAccountOrderAsync();
            AccountProfileMessage = "\u76ae\u80a4\u5df2\u66f4\u65b0";
            await LoadSelectedAccountProfileAsync(updatedAccount);
        }
        catch (Exception ex)
        {
            AccountProfileMessage = $"\u66f4\u6362\u76ae\u80a4\u5931\u8d25\uff1a{ex.Message}";
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
            AccountProfileMessage = "\u53ea\u6709\u6b63\u7248\u8d26\u6237\u53ef\u4ee5\u66f4\u6362\u62ab\u98ce";
            return;
        }

        try
        {
            IsAccountProfileBusy = true;
            AccountProfileMessage = "\u6b63\u5728\u66f4\u6362\u62ab\u98ce...";
            await microsoftAccountService.SetActiveCapeAsync(account, cape.Id);
            AccountProfileMessage = cape.IsNone ? "\u5df2\u79fb\u9664\u5f53\u524d\u62ab\u98ce" : $"\u5df2\u66f4\u6362\u62ab\u98ce\uff1a{cape.DisplayName}";
            MarkSelectedCapeActive(cape);
            await StoreSelectedAccountCapeCacheAsync();
        }
        catch (Exception ex)
        {
            AccountProfileMessage = $"\u66f4\u6362\u62ab\u98ce\u5931\u8d25\uff1a{ex.Message}";
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
        ReportStatus("\u6b63\u5728\u6253\u5f00 Microsoft \u767b\u5f55\u9875\u9762...");
    }

    public async Task CompleteMicrosoftAccountLoginAsync()
    {
        try
        {
            var account = await microsoftAccountService.LoginInteractivelyAsync();
            if (string.IsNullOrWhiteSpace(account.DisplayName) || string.IsNullOrWhiteSpace(account.Uuid))
            {
                var message = "\u6b63\u7248\u767b\u5f55\u5931\u8d25\uff1a\u672a\u83b7\u53d6\u5230 Minecraft Java \u8d26\u6237\u8d44\u6599";
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
                var message = $"\u6b63\u7248\u8d26\u6237 {existing.DisplayName} \u5df2\u7ecf\u6dfb\u52a0\u8fc7\u4e86\uff0c\u5df2\u4e3a\u4f60\u9009\u4e2d";
                ReportStatus(message);
                ShowMicrosoftLoginResult(true, message, alreadyAdded: true);
                return;
            }

            Accounts.Add(account);
            SelectAccount(account, persistSelection: false);
            await PersistAccountOrderAsync();
            var addedMessage = $"\u5df2\u6dfb\u52a0\u6b63\u7248\u8d26\u6237 {account.DisplayName}";
            ReportStatus(addedMessage);
            ShowMicrosoftLoginResult(true, addedMessage);
        }
        catch (OperationCanceledException)
        {
            const string message = "\u6b63\u7248\u767b\u5f55\u5df2\u53d6\u6d88";
            ReportStatus(message);
            ShowMicrosoftLoginResult(false, message);
        }
        catch (Exception ex)
        {
            var message = $"\u6b63\u7248\u767b\u5f55\u5931\u8d25\uff1a{ex.Message}";
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
            ReportStatus(AccountNameValidationMessage);
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
        ReportStatus($"\u5df2\u6dfb\u52a0\u79bb\u7ebf\u8d26\u6237 {accountName}");
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
        ReportStatus($"\u5df2\u5220\u9664\u8d26\u6237 {deletedName}");

        try
        {
            await PersistAccountOrderAsync();
            if (!account.IsOffline)
                await microsoftAccountService.DeleteAccountAsync(account);
        }
        catch (Exception ex)
        {
            ReportStatus($"\u5df2\u4ece\u5217\u8868\u5220\u9664\u8d26\u6237 {deletedName}\uff0c\u4f46\u6e05\u7406\u767b\u5f55\u7f13\u5b58\u5931\u8d25\uff1a{ex.Message}");
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
            ReportStatus(AccountNameValidationMessage);
            return;
        }

        if (string.Equals(newName, account.DisplayName, StringComparison.Ordinal))
        {
            ShowRenameAccountResult(true, "\u8d26\u6237\u540d\u6ca1\u6709\u53d8\u5316");
            return;
        }

        try
        {
            IsRenameAccountDialogBusy = true;
            RenameAccountDialogStep = AccountDialogSteps.RenameStatus;
            RenameAccountIcon = DialogBusyIcon;
            RenameAccountMessage = account.IsOffline
                ? "\u6b63\u5728\u4fdd\u5b58\u79bb\u7ebf\u8d26\u6237\u540d..."
                : "\u6b63\u5728\u8054\u7f51\u4fee\u6539\u6b63\u7248\u8d26\u6237\u540d...";

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
            var message = $"\u5df2\u5c06\u8d26\u6237\u540d\u4fee\u6539\u4e3a {updatedAccount.DisplayName}";
            ReportStatus(message);
            ShowRenameAccountResult(true, $"\u8d26\u6237\u540d\u5df2\u4fee\u6539\u4e3a {updatedAccount.DisplayName}");
        }
        catch (Exception ex)
        {
            ReportStatus(ex.Message);
            ShowRenameAccountResult(false, ex.Message);
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
            AccountProfileMessage = "\u79bb\u7ebf\u8d26\u6237\u4e0d\u652f\u6301\u76ae\u80a4\u548c\u62ab\u98ce\u7ba1\u7406";
            return;
        }

        try
        {
            IsAccountProfileBusy = true;
            AccountProfileMessage = "\u6b63\u5728\u8bfb\u53d6\u6b63\u7248\u8d26\u6237\u8d44\u6599...";
            var capes = await microsoftAccountService.GetCapesAsync(account);
            if (!ReferenceEquals(SelectedAccount, account))
                return;

            PopulateSelectedAccountCapeOptions(capes);
            await StoreSelectedAccountCapeCacheAsync();
            OnPropertyChanged(nameof(CanApplySelectedCape));
            AccountProfileMessage = SelectedAccountCapeOptions.Count == 0
                ? "\u8fd9\u4e2a\u8d26\u6237\u6ca1\u6709\u53ef\u7528\u62ab\u98ce"
                : "\u6b63\u7248\u8d26\u6237\u8d44\u6599\u5df2\u52a0\u8f7d";
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(SelectedAccount, account))
                AccountProfileMessage = $"\u8bfb\u53d6\u6b63\u7248\u8d26\u6237\u8d44\u6599\u5931\u8d25\uff1a{ex.Message}";
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
            ? "\u79bb\u7ebf\u8d26\u6237\u4e0d\u652f\u6301\u76ae\u80a4\u548c\u62ab\u98ce\u7ba1\u7406"
            : SelectedAccountCapeOptions.Count > 0
                ? "\u5df2\u52a0\u8f7d\u672c\u5730\u62ab\u98ce\u7f13\u5b58\uff0c\u70b9\u51fb\u5237\u65b0\u53ef\u83b7\u53d6\u6700\u65b0\u72b6\u6001"
                : "\u70b9\u51fb\u5237\u65b0\u8bfb\u53d6\u8fd9\u4e2a\u6b63\u7248\u8d26\u6237\u7684\u62ab\u98ce\u5217\u8868";
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

    private void ResetMicrosoftLoginResultState(string message = MicrosoftLoginInitialMessage)
    {
        IsMicrosoftLoginSuccessful = false;
        IsMicrosoftAccountAlreadyAdded = false;
        MicrosoftLoginIcon = DialogBusyIcon;
        MicrosoftLoginMessage = message;
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
        return AccountNameRegex.IsMatch(accountName);
    }

    private void ReportStatus(string message)
    {
        statusService.Report(message);
    }

}
