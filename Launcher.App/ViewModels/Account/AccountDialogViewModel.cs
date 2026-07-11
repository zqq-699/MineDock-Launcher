/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Application.Accounts;
using Launcher.App.Models;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.Account;

/// <summary>
/// 协调账户新增、删除和重命名对话框的多步骤 UI 状态，并把实际账户操作委托给账户服务与列表模型。
/// </summary>
public sealed partial class AccountDialogViewModel : ObservableObject
{
    // 图标与步骤字符串共同构成对话框状态机；集中定义可避免各分支用不同值表达同一状态。
    private const string DialogBusyIcon = "\uE895";
    private const string DialogSuccessIcon = "\uE73E";
    private const string DialogFailureIcon = "\uE783";
    private const string RenameInputIcon = "\uE70F";

    private static string MicrosoftLoginInitialMessage => Strings.Status_OpeningMicrosoftLogin;
    private static string MicrosoftLoginActiveMessage => Strings.Status_LoginMicrosoftActive;

    // AccountListViewModel 是当前账户集合和持久化顺序的唯一 UI 所有者，本类只编排对话框流程。
    private readonly AccountListViewModel accountList;
    private readonly IMicrosoftAccountService microsoftAccountService;
    private readonly IThirdPartyAccountService thirdPartyAccountService;
    private readonly IOfflineAccountUuidService offlineUuidService;
    private readonly IStatusService statusService;
    private readonly ILogger<AccountDialogViewModel> logger;

    // 新增账户流程：类型选择 -> 离线名称或 Microsoft 登录 -> 结果。Busy 时禁止关闭和回退，
    // 防止交互登录尚未返回时 UI 被重置，随后又把过期结果写回新一轮对话框。
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
    private LauncherAccount? accountPendingMicrosoftReauthentication;

    [ObservableProperty]
    private LauncherAccount? accountPendingThirdPartyReauthentication;

    [ObservableProperty] private string thirdPartyImportCurrentProfileName = string.Empty;
    [ObservableProperty] private int thirdPartyImportCompletedCount;
    [ObservableProperty] private int thirdPartyImportTotalCount;
    [ObservableProperty] private int thirdPartyImportFailedCount;
    private readonly List<ThirdPartyProfileOptionViewModel> thirdPartyFailedProfiles = [];
    private readonly List<LauncherAccount> thirdPartySuccessfulAccounts = [];
    private CancellationTokenSource? thirdPartyImportCancellationTokenSource;

    // 删除对话框只保留待删除对象；确认后会立即清空引用，避免重复确认触发两次删除。
    [ObservableProperty]
    private bool isDeleteAccountDialogOpen;

    [ObservableProperty]
    private LauncherAccount? accountPendingDelete;

    // 重命名流程同时服务离线与 Microsoft 账户。两者共享校验和结果页，但底层身份更新规则不同。
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
    private string renameAccountErrorCodeMessage = string.Empty;

    [ObservableProperty]
    private string renameAccountIcon = RenameInputIcon;

    public AccountDialogViewModel(
        AccountListViewModel accountList,
        IMicrosoftAccountService microsoftAccountService,
        IThirdPartyAccountService thirdPartyAccountService,
        IOfflineAccountUuidService offlineUuidService,
        IStatusService statusService,
        ILogger<AccountDialogViewModel>? logger = null)
    {
        this.accountList = accountList;
        this.microsoftAccountService = microsoftAccountService;
        this.thirdPartyAccountService = thirdPartyAccountService;
        this.offlineUuidService = offlineUuidService;
        this.statusService = statusService;
        this.logger = logger ?? NullLogger<AccountDialogViewModel>.Instance;
        ThirdParty = new ThirdPartyAccountDialogViewModel(accountList, thirdPartyAccountService, this.logger);
        ThirdParty.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ThirdPartyAccountDialogViewModel.CanConfirm)
                or nameof(ThirdPartyAccountDialogViewModel.HasSelectedProfiles))
                OnPropertyChanged(nameof(CanConfirmAddAccountDialog));
            if (e.PropertyName == nameof(ThirdPartyAccountDialogViewModel.CanSelectAllProfiles))
                OnPropertyChanged(nameof(CanSelectAllThirdPartyProfiles));
        };
    }

    public ObservableCollection<AccountTypeOption> AccountTypeOptions { get; } = new(AccountTypeOptionFactory.Create());
    public ThirdPartyAccountDialogViewModel ThirdParty { get; }

    public bool IsAccountTypeStep => AddAccountDialogStep == AccountDialogSteps.AddAccountType;
    public bool IsOfflineNameStep => AddAccountDialogStep == AccountDialogSteps.AddAccountOfflineName;
    public bool IsThirdPartyCredentialsStep => AddAccountDialogStep == AccountDialogSteps.AddAccountThirdPartyCredentials;
    public bool IsThirdPartyReauthenticationStep => AddAccountDialogStep == AccountDialogSteps.AddAccountThirdPartyReauthentication;
    public bool IsThirdPartyFormStep => IsThirdPartyCredentialsStep || IsThirdPartyReauthenticationStep;
    public bool IsThirdPartyProfileSelectionStep => AddAccountDialogStep == AccountDialogSteps.AddAccountThirdPartyProfileSelection;
    public bool IsThirdPartyImportProgressStep => AddAccountDialogStep == AccountDialogSteps.AddAccountThirdPartyImportProgress;
    public bool IsThirdPartyImportResultStep => AddAccountDialogStep == AccountDialogSteps.AddAccountThirdPartyImportResult;
    public bool IsMicrosoftLoginStep => AddAccountDialogStep == AccountDialogSteps.AddAccountMicrosoftLogin;
    public bool IsMicrosoftLoginResultStep => AddAccountDialogStep == AccountDialogSteps.AddAccountMicrosoftResult;
    public bool IsMicrosoftReauthenticationStep => AddAccountDialogStep == AccountDialogSteps.AddAccountMicrosoftReauthentication;
    public bool IsMicrosoftReauthenticationResultStep => AddAccountDialogStep == AccountDialogSteps.AddAccountMicrosoftReauthenticationResult;
    public bool IsMicrosoftReauthenticationMode => AccountPendingMicrosoftReauthentication is not null;
    public bool IsMicrosoftStatusStep => IsMicrosoftLoginStep || IsMicrosoftLoginResultStep
        || IsMicrosoftReauthenticationStep || IsMicrosoftReauthenticationResultStep;
    public bool CanShowAddAccountBackButton => !IsAddAccountDialogBusy
        && (IsOfflineNameStep || IsThirdPartyCredentialsStep || IsMicrosoftLoginStep);
    public bool CanShowAddAccountCancelButton => !IsAddAccountDialogBusy
        && (!IsMicrosoftLoginResultStep || IsMicrosoftReauthenticationMode);
    public bool IsAddAccountFooterEnabled => !IsAddAccountDialogBusy;
    public bool CanConfirmAddAccountDialog => !IsAddAccountDialogBusy
        && (IsMicrosoftLoginResultStep
            || IsMicrosoftReauthenticationResultStep
            || IsOfflineNameStep
            || (IsThirdPartyFormStep && ThirdParty.CanConfirm)
            || (IsThirdPartyProfileSelectionStep && ThirdParty.HasSelectedProfiles)
            || IsThirdPartyImportResultStep
            || (IsAccountTypeStep && SelectedAccountTypeOption is not null));
    public bool IsMicrosoftAccountTypeSelected => IsAccountTypeStep && SelectedAccountTypeOption?.Kind is AccountTypeKinds.Microsoft;

    public bool IsRenameAccountInputStep => RenameAccountDialogStep == AccountDialogSteps.RenameInput;
    public bool IsRenameAccountStatusStep => RenameAccountDialogStep == AccountDialogSteps.RenameStatus;
    public bool IsRenameAccountResultStep => RenameAccountDialogStep == AccountDialogSteps.RenameResult;
    public bool IsRenameAccountMessageStep => IsRenameAccountStatusStep || IsRenameAccountResultStep;
    public bool IsRenameMicrosoftAccount => AccountPendingRename?.IsMicrosoft == true;
    public bool CanShowRenameAccountCancelButton => !IsRenameAccountDialogBusy && IsRenameAccountInputStep;
    public bool CanConfirmRenameAccountDialog => !IsRenameAccountDialogBusy
        && (IsRenameAccountResultStep || (IsRenameAccountInputStep && !string.IsNullOrWhiteSpace(RenameAccountName)));
    public bool HasRenameAccountErrorCode => !string.IsNullOrWhiteSpace(RenameAccountErrorCodeMessage);

    public string? MicrosoftLoginIconKey => IsMicrosoftLoginStep || IsMicrosoftReauthenticationStep
        ? "general/general_external-web"
        : IsMicrosoftLoginResultStep || IsMicrosoftReauthenticationResultStep
            ? IsMicrosoftLoginSuccessful ? "general/general_passed" : "general/general_attention"
            : null;

    public string? RenameAccountIconKey => IsRenameAccountResultStep
        ? IsRenameAccountSuccessful ? "general/general_passed" : "general/general_attention"
        : null;

    public string RenameAccountDialogTitle =>
        AccountDialogText.GetRenameTitle(RenameAccountDialogStep, IsRenameAccountSuccessful);

    public string RenameAccountDialogSubtitle =>
        AccountDialogText.GetRenameSubtitle(RenameAccountDialogStep, IsRenameMicrosoftAccount);

    public string AddAccountDialogTitle => AddAccountDialogStep switch
    {
        AccountDialogSteps.AddAccountMicrosoftReauthentication => Strings.Dialog_ReauthenticateMicrosoftAccountTitle,
        AccountDialogSteps.AddAccountMicrosoftReauthenticationResult => Strings.Dialog_ReauthenticateMicrosoftAccountTitle,
        AccountDialogSteps.AddAccountThirdPartyReauthentication => Strings.Dialog_ReauthenticateThirdPartyAccountTitle,
        AccountDialogSteps.AddAccountThirdPartyProfileSelection => Strings.Dialog_ThirdPartyProfileSelectionTitle,
        AccountDialogSteps.AddAccountThirdPartyImportProgress => Strings.Dialog_ThirdPartyImportProgressTitle,
        AccountDialogSteps.AddAccountThirdPartyImportResult => Strings.Dialog_ThirdPartyImportResultTitle,
        _ => AccountDialogText.GetAddTitle(
            AddAccountDialogStep,
            IsMicrosoftAccountAlreadyAdded,
            IsMicrosoftLoginSuccessful)
    };

    public string AddAccountDialogSubtitle => AddAccountDialogStep switch
    {
        AccountDialogSteps.AddAccountMicrosoftReauthentication => Strings.Dialog_ReauthenticateMicrosoftAccountSubtitle,
        AccountDialogSteps.AddAccountMicrosoftReauthenticationResult => Strings.Dialog_ReauthenticateMicrosoftAccountSubtitle,
        AccountDialogSteps.AddAccountThirdPartyReauthentication => Strings.Dialog_ReauthenticateThirdPartyAccountSubtitle,
        AccountDialogSteps.AddAccountThirdPartyProfileSelection => Strings.Dialog_ThirdPartyProfileSelectionSubtitle,
        AccountDialogSteps.AddAccountThirdPartyImportProgress => Strings.Dialog_ThirdPartyImportProgressSubtitle,
        AccountDialogSteps.AddAccountThirdPartyImportResult => Strings.Dialog_ThirdPartyImportResultSubtitle,
        _ => AccountDialogText.GetAddSubtitle(AddAccountDialogStep)
    };

    public bool IsThirdPartyIdentityReadOnly => IsThirdPartyReauthenticationStep;
    public bool CanSelectAllThirdPartyProfiles => IsThirdPartyProfileSelectionStep && ThirdParty.CanSelectAllProfiles;
    public bool CanShowStandardAddAccountFooter => !IsThirdPartyImportProgressStep && !IsThirdPartyImportResultStep;
    public string AddAccountConfirmButtonText => IsMicrosoftReauthenticationResultStep
        ? Strings.Retry_Button
        : Strings.Confirm_Button;
    public string ThirdPartyImportProgressText => string.Format(
        Strings.Dialog_ThirdPartyImportProgressFormat,
        ThirdPartyImportCompletedCount,
        ThirdPartyImportTotalCount);
    public string ThirdPartyImportFailureText => string.Format(
        Strings.Dialog_ThirdPartyImportFailureFormat,
        ThirdPartyImportFailedCount);

    public void OpenAddAccountDialog()
    {
        ResetAddAccountDialogState(clearOfflineName: true);
        IsAddAccountDialogOpen = true;
    }

    public void OpenThirdPartyReauthenticationDialog(LauncherAccount account)
    {
        ResetAddAccountDialogState(clearOfflineName: true);
        AccountPendingThirdPartyReauthentication = account;
        AddAccountDialogStep = AccountDialogSteps.AddAccountThirdPartyReauthentication;
        ThirdParty.PrepareReauthentication(account);
        IsAddAccountDialogOpen = true;
    }

    public void OpenMicrosoftReauthenticationDialog(LauncherAccount account)
    {
        ResetAddAccountDialogState(clearOfflineName: true);
        AccountPendingMicrosoftReauthentication = account;
        AddAccountDialogStep = AccountDialogSteps.AddAccountMicrosoftReauthentication;
        IsAddAccountDialogBusy = true;
        ResetMicrosoftLoginResultState(MicrosoftLoginActiveMessage);
        IsAddAccountDialogOpen = true;
        ReportStatus(Strings.Status_OpeningMicrosoftLogin);
    }

    public void CancelAddAccountDialog()
    {
        if (IsThirdPartyImportProgressStep)
        {
            thirdPartyImportCancellationTokenSource?.Cancel();
            return;
        }
        if (IsAddAccountDialogBusy)
            return;

        IsAddAccountDialogOpen = false;
        _ = ThirdParty.CancelEmailLoginAsync();
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
        // 先进入 Busy 状态再启动外部浏览器登录，保证按钮状态和状态文案在异步边界前完成切换。
        AddAccountDialogStep = AccountDialogSteps.AddAccountMicrosoftLogin;
        IsAddAccountDialogBusy = true;
        ResetMicrosoftLoginResultState(MicrosoftLoginActiveMessage);
        ReportStatus(Strings.Status_OpeningMicrosoftLogin);
    }

    public async Task CompleteMicrosoftAccountLoginAsync()
    {
        try
        {
            // 服务返回的资料仍需在 UI 边界校验；缺少名称或 UUID 的响应不能进入账户集合。
            var account = await microsoftAccountService.LoginInteractivelyAsync();
            if (string.IsNullOrWhiteSpace(account.DisplayName) || string.IsNullOrWhiteSpace(account.Uuid))
            {
                var message = Strings.Status_LoginMissingProfile;
                ReportStatus(message);
                ShowMicrosoftLoginResult(false, message);
                return;
            }

            // Microsoft UUID 才是稳定身份。重复登录时选中已有账户并刷新顺序，不能创建名称相同的副本。
            var existing = accountList.Accounts.FirstOrDefault(item =>
                item.Account.IsMicrosoft && string.Equals(item.Uuid, account.Uuid, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                accountList.SelectItem(existing, persistSelection: false);
                await accountList.PersistAccountOrderAsync();
                var message = string.Format(Strings.Status_LoginAccountAlreadyAddedFormat, existing.DisplayName);
                ReportStatus(message);
                ShowMicrosoftLoginResult(true, message, alreadyAdded: true);
                return;
            }

            await accountList.AddAndSelectAsync(account);
            var addedMessage = string.Format(Strings.Status_LoginAccountAddedFormat, account.DisplayName);
            ReportStatus(addedMessage);
            ShowMicrosoftLoginResult(true, addedMessage);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Microsoft account login canceled.");
            var message = Strings.Status_LoginCanceled;
            ReportStatus(message);
            ShowMicrosoftLoginResult(false, message);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Microsoft account login failed.");
            var message = Strings.Status_LoginFailed;
            ReportStatus(message);
            ShowMicrosoftLoginResult(false, message);
        }
        finally
        {
            // 所有成功、取消和异常路径都必须解除 Busy，否则对话框会永久失去关闭能力。
            IsAddAccountDialogBusy = false;
        }
    }

    public void CloseAddAccountDialogAfterMicrosoftResult()
    {
        if (IsAddAccountDialogBusy)
            return;

        IsAddAccountDialogOpen = false;
    }

    public async Task ConfirmAddAccountDialogAsync(string? thirdPartyPassword = null)
    {
        if (IsAddAccountDialogBusy)
            return;

        if (IsMicrosoftLoginResultStep)
        {
            CloseAddAccountDialogAfterMicrosoftResult();
            return;
        }

        if (IsThirdPartyImportResultStep)
        {
            IsAddAccountDialogOpen = false;
            await ThirdParty.CancelEmailLoginAsync();
            return;
        }

        if (IsThirdPartyProfileSelectionStep)
        {
            await ImportThirdPartyProfilesAsync(
                ThirdParty.Profiles.Where(profile => profile.IsSelected).ToArray(),
                thirdPartyPassword ?? string.Empty);
            return;
        }

        if (IsThirdPartyReauthenticationStep && AccountPendingThirdPartyReauthentication is { } pendingAccount)
        {
            IsAddAccountDialogBusy = true;
            try
            {
                var authenticated = await ThirdParty.ReauthenticateAsync(
                    pendingAccount,
                    thirdPartyPassword ?? string.Empty);
                if (authenticated is not null)
                {
                    await accountList.ReplaceSelectedAccountAndPersistAsync(pendingAccount, authenticated);
                    IsAddAccountDialogOpen = false;
                }
            }
            finally
            {
                IsAddAccountDialogBusy = false;
            }
            return;
        }

        if (SelectedAccountTypeOption is null)
            return;

        // “确认”按钮在不同步骤含义不同：第一页只负责推进状态，真正新增发生在名称页。
        if (IsAccountTypeStep)
        {
            if (SelectedAccountTypeOption.Kind is AccountTypeKinds.Offline)
            {
                AddAccountDialogStep = AccountDialogSteps.AddAccountOfflineName;
                return;
            }

            if (SelectedAccountTypeOption.Kind is AccountTypeKinds.ThirdParty)
            {
                AddAccountDialogStep = AccountDialogSteps.AddAccountThirdPartyCredentials;
                return;
            }

            return;
        }

        if (IsThirdPartyCredentialsStep)
        {
            IsAddAccountDialogBusy = true;
            try
            {
                if (ThirdParty.IsEmailIdentifier)
                {
                    if (await ThirdParty.BeginEmailLoginAsync(thirdPartyPassword ?? string.Empty))
                        AddAccountDialogStep = AccountDialogSteps.AddAccountThirdPartyProfileSelection;
                }
                else if (await ThirdParty.LoginAsync(thirdPartyPassword ?? string.Empty))
                {
                    IsAddAccountDialogOpen = false;
                }
            }
            finally
            {
                IsAddAccountDialogBusy = false;
            }
            return;
        }

        var accountName = NewOfflineAccountName.Trim();
        if (!AccountNameValidator.IsValid(accountName))
        {
            IsNewOfflineAccountNameInvalid = true;
            ReportStatus(AccountNameValidator.ValidationMessage);
            return;
        }

        // 离线账户没有服务端身份，使用随机内部 Id 区分同名历史记录，游戏 UUID 则按标准算法生成。
        var account = new LauncherAccount
        {
            Id = $"offline-{Guid.NewGuid():N}",
            DisplayName = accountName,
            Uuid = offlineUuidService.CreateUuid(accountName, OfflineUuidGenerationMode.Standard),
            OfflineUuidGenerationMode = OfflineUuidGenerationMode.Standard,
            Kind = LauncherAccountKind.Offline
        };

        await accountList.AddAndSelectAsync(account);
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

        // 先从可见列表移除并关闭对话框，再清理 Microsoft 缓存；缓存清理失败不应把已删除账户重新插回。
        var removeTask = accountList.RemoveAsync(account);
        IsDeleteAccountDialogOpen = false;
        AccountPendingDelete = null;
        ReportStatus(string.Format(Strings.Status_AccountDeletedFormat, deletedName));

        try
        {
            await removeTask;
            if (account.IsMicrosoft)
                await microsoftAccountService.DeleteAccountAsync(account);
            else if (account.IsThirdParty)
                await thirdPartyAccountService.DeleteCredentialsAsync(account.Id);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Account delete cleanup failed. AccountId={AccountId} IsOffline={IsOffline}", account.Id, account.IsOffline);
            ReportStatus(string.Format(Strings.Status_AccountDeletedCacheCleanupFailedFormat, deletedName));
        }
    }

    public void OpenRenameAccountDialog()
    {
        if (accountList.SelectedAccount is null || accountList.SelectedAccount.IsThirdParty)
            return;

        AccountPendingRename = accountList.SelectedAccount;
        ResetRenameAccountDialogState(accountList.SelectedAccount.DisplayName);
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
        if (account.IsThirdParty)
        {
            IsRenameAccountDialogOpen = false;
            return;
        }

        var newName = RenameAccountName.Trim();
        if (!AccountNameValidator.IsValid(newName))
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
            // 状态页在远端操作期间替代输入页，既阻止重复提交，也向用户说明可能较慢的网络步骤。
            IsRenameAccountDialogBusy = true;
            RenameAccountDialogStep = AccountDialogSteps.RenameStatus;
            RenameAccountIcon = DialogBusyIcon;
            RenameAccountMessage = account.IsOffline
                ? Strings.Status_SavingOfflineAccountName
                : Strings.Status_ChangingMicrosoftAccountName;

            LauncherAccount updatedAccount;
            if (account.IsOffline)
            {
                // 离线名称参与 UUID 生成；保留原生成模式，才能兼容不同历史版本创建的账户。
                var uuid = offlineUuidService.CreateUuid(
                    newName,
                    account.OfflineUuidGenerationMode,
                    account.Uuid);
                updatedAccount = AccountMapper.WithDisplayNameAndOfflineUuid(
                    account,
                    newName,
                    account.OfflineUuidGenerationMode,
                    uuid);
            }
            else
            {
                // Microsoft 重命名由远端服务执行，不能只修改本地显示名，否则下次刷新会被服务端覆盖。
                var renamedAccount = await microsoftAccountService.ChangeNameAsync(account, newName);
                updatedAccount = AccountMapper.WithCapeCache(
                    AccountMapper.WithAppearanceFallback(renamedAccount, account),
                    account.CachedCapeOptions);
            }

            accountList.ReplaceSelectedAccount(account, updatedAccount);
            AccountPendingRename = updatedAccount;
            await accountList.PersistAccountOrderAsync();
            var message = string.Format(Strings.Status_AccountRenamedFormat, updatedAccount.DisplayName);
            ReportStatus(message);
            ShowRenameAccountResult(true, string.Format(Strings.Status_AccountRenameResultFormat, updatedAccount.DisplayName));
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Account rename failed. AccountId={AccountId} IsOffline={IsOffline} ErrorCode={ErrorCode}",
                account.Id,
                account.IsOffline,
                AccountErrorCodeMessageFormatter.Format(ex));
            var message = GetRenameFailureMessage(ex);
            var errorCodeMessage = AccountErrorCodeMessageFormatter.Format(ex);
            ReportStatus(message);
            ShowRenameAccountResult(false, message, errorCodeMessage);
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

    partial void OnAccountPendingMicrosoftReauthenticationChanged(LauncherAccount? value)
    {
        OnPropertyChanged(nameof(IsMicrosoftReauthenticationMode));
        NotifyAddAccountDialogActionPropertiesChanged();
    }

    partial void OnSelectedAccountTypeOptionChanged(AccountTypeOption? value)
    {
        OnPropertyChanged(nameof(IsMicrosoftAccountTypeSelected));
        OnPropertyChanged(nameof(CanConfirmAddAccountDialog));
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

    partial void OnRenameAccountErrorCodeMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasRenameAccountErrorCode));
    }

    partial void OnNewOfflineAccountNameChanged(string value)
    {
        IsNewOfflineAccountNameInvalid = false;
    }

    partial void OnThirdPartyImportCompletedCountChanged(int value) =>
        OnPropertyChanged(nameof(ThirdPartyImportProgressText));

    partial void OnThirdPartyImportTotalCountChanged(int value) =>
        OnPropertyChanged(nameof(ThirdPartyImportProgressText));

    partial void OnThirdPartyImportFailedCountChanged(int value) =>
        OnPropertyChanged(nameof(ThirdPartyImportFailureText));

    public void SelectAllThirdPartyProfiles() => ThirdParty.SelectAllProfiles();

    public Task RetryThirdPartyProfileImportAsync(string password) =>
        ImportThirdPartyProfilesAsync(thirdPartyFailedProfiles.ToArray(), password);

    private async Task ImportThirdPartyProfilesAsync(
        IReadOnlyList<ThirdPartyProfileOptionViewModel> profiles,
        string password)
    {
        if (profiles.Count == 0)
            return;
        thirdPartyFailedProfiles.Clear();
        ThirdPartyImportFailedCount = 0;
        ThirdPartyImportCompletedCount = 0;
        ThirdPartyImportTotalCount = profiles.Count;
        AddAccountDialogStep = AccountDialogSteps.AddAccountThirdPartyImportProgress;
        IsAddAccountDialogBusy = true;
        using var cancellation = new CancellationTokenSource();
        thirdPartyImportCancellationTokenSource = cancellation;
        try
        {
            foreach (var profile in profiles)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                ThirdPartyImportCurrentProfileName = profile.Name;
                var imported = await ThirdParty.ImportEmailProfileAsync(profile, password, cancellation.Token);
                if (imported is null)
                {
                    thirdPartyFailedProfiles.Add(profile);
                }
                else if (thirdPartySuccessfulAccounts.All(account => !string.Equals(account.Id, imported.Id, StringComparison.Ordinal)))
                {
                    thirdPartySuccessfulAccounts.Add(imported);
                }
                ThirdPartyImportCompletedCount++;
            }

            await SelectFirstSuccessfulThirdPartyAccountAsync();
            ThirdPartyImportFailedCount = thirdPartyFailedProfiles.Count;
            if (thirdPartyFailedProfiles.Count == 0)
            {
                IsAddAccountDialogOpen = false;
                await ThirdParty.CancelEmailLoginAsync();
            }
            else
            {
                AddAccountDialogStep = AccountDialogSteps.AddAccountThirdPartyImportResult;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            await SelectFirstSuccessfulThirdPartyAccountAsync();
            IsAddAccountDialogOpen = false;
            await ThirdParty.CancelEmailLoginAsync();
        }
        finally
        {
            thirdPartyImportCancellationTokenSource = null;
            IsAddAccountDialogBusy = false;
        }
    }

    public async Task<bool> CompleteMicrosoftAccountReauthenticationAsync()
    {
        var account = AccountPendingMicrosoftReauthentication;
        if (account is null)
            return false;

        AddAccountDialogStep = AccountDialogSteps.AddAccountMicrosoftReauthentication;
        IsAddAccountDialogBusy = true;
        ResetMicrosoftLoginResultState(MicrosoftLoginActiveMessage);
        try
        {
            var refreshed = await microsoftAccountService.ReauthenticateInteractivelyAsync(account);
            await accountList.ReplaceSelectedAccountAndPersistAsync(account, refreshed);
            AccountPendingMicrosoftReauthentication = refreshed;
            ReportStatus(Strings.Status_MicrosoftReauthenticationSuccessful);
            IsAddAccountDialogOpen = false;
            return true;
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Microsoft account reauthentication canceled. AccountId={AccountId}", account.Id);
            ShowMicrosoftReauthenticationFailure(Strings.Status_LoginCanceled);
            return false;
        }
        catch (MicrosoftAccountReauthenticationException exception)
        {
            logger.LogWarning(exception, "Microsoft account reauthentication failed. AccountId={AccountId} Reason={Reason}", account.Id, exception.Reason);
            var message = exception.Reason switch
            {
                MicrosoftAccountReauthenticationFailureReason.AccountMismatch => Strings.Status_MicrosoftReauthenticationAccountMismatch,
                MicrosoftAccountReauthenticationFailureReason.CredentialStorageFailed => Strings.Status_MicrosoftCredentialStorageFailed,
                _ => Strings.Status_LoginFailed
            };
            ShowMicrosoftReauthenticationFailure(message);
            return false;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Microsoft account reauthentication failed. AccountId={AccountId}", account.Id);
            ShowMicrosoftReauthenticationFailure(Strings.Status_LoginFailed);
            return false;
        }
        finally
        {
            IsAddAccountDialogBusy = false;
        }
    }

    private async Task SelectFirstSuccessfulThirdPartyAccountAsync()
    {
        if (thirdPartySuccessfulAccounts.Count == 0)
            return;
        var first = accountList.FindAccount(thirdPartySuccessfulAccounts[0].Id);
        if (first is null)
            return;
        accountList.SelectAccount(first, persistSelection: false);
        await accountList.PersistAccountOrderAsync();
    }

    private void NotifyAddAccountDialogStepPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsAccountTypeStep));
        OnPropertyChanged(nameof(IsOfflineNameStep));
        OnPropertyChanged(nameof(IsThirdPartyCredentialsStep));
        OnPropertyChanged(nameof(IsThirdPartyReauthenticationStep));
        OnPropertyChanged(nameof(IsThirdPartyFormStep));
        OnPropertyChanged(nameof(IsThirdPartyProfileSelectionStep));
        OnPropertyChanged(nameof(IsThirdPartyImportProgressStep));
        OnPropertyChanged(nameof(IsThirdPartyImportResultStep));
        OnPropertyChanged(nameof(CanSelectAllThirdPartyProfiles));
        OnPropertyChanged(nameof(CanShowStandardAddAccountFooter));
        OnPropertyChanged(nameof(IsThirdPartyIdentityReadOnly));
        OnPropertyChanged(nameof(IsMicrosoftLoginStep));
        OnPropertyChanged(nameof(IsMicrosoftLoginResultStep));
        OnPropertyChanged(nameof(IsMicrosoftReauthenticationStep));
        OnPropertyChanged(nameof(IsMicrosoftReauthenticationResultStep));
        OnPropertyChanged(nameof(IsMicrosoftStatusStep));
        OnPropertyChanged(nameof(MicrosoftLoginIconKey));
        OnPropertyChanged(nameof(AddAccountConfirmButtonText));
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

    private void ResetAddAccountDialogState(bool clearOfflineName)
    {
        thirdPartyImportCancellationTokenSource?.Cancel();
        thirdPartyImportCancellationTokenSource = null;
        thirdPartyFailedProfiles.Clear();
        thirdPartySuccessfulAccounts.Clear();
        ThirdPartyImportCurrentProfileName = string.Empty;
        ThirdPartyImportCompletedCount = 0;
        ThirdPartyImportTotalCount = 0;
        ThirdPartyImportFailedCount = 0;
        AccountPendingThirdPartyReauthentication = null;
        AccountPendingMicrosoftReauthentication = null;
        AddAccountDialogStep = AccountDialogSteps.AddAccountType;
        if (clearOfflineName)
        {
            NewOfflineAccountName = string.Empty;
            ThirdParty.Reset();
        }

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
        RenameAccountErrorCodeMessage = string.Empty;
    }

    private void ShowMicrosoftLoginResult(bool isSuccess, string message, bool alreadyAdded = false)
    {
        IsMicrosoftLoginSuccessful = isSuccess;
        IsMicrosoftAccountAlreadyAdded = alreadyAdded;
        MicrosoftLoginIcon = isSuccess ? DialogSuccessIcon : DialogFailureIcon;
        MicrosoftLoginMessage = message;
        AddAccountDialogStep = AccountDialogSteps.AddAccountMicrosoftResult;
    }

    private void ShowMicrosoftReauthenticationFailure(string message)
    {
        IsMicrosoftLoginSuccessful = false;
        IsMicrosoftAccountAlreadyAdded = false;
        MicrosoftLoginIcon = DialogFailureIcon;
        MicrosoftLoginMessage = message;
        AddAccountDialogStep = AccountDialogSteps.AddAccountMicrosoftReauthenticationResult;
        ReportStatus(message);
    }

    private void ShowRenameAccountResult(bool isSuccess, string message, string errorCodeMessage = "")
    {
        IsRenameAccountSuccessful = isSuccess;
        RenameAccountIcon = isSuccess ? DialogSuccessIcon : DialogFailureIcon;
        RenameAccountMessage = message;
        RenameAccountErrorCodeMessage = isSuccess ? string.Empty : errorCodeMessage;
        RenameAccountDialogStep = AccountDialogSteps.RenameResult;
    }

    private static string GetRenameFailureMessage(Exception exception)
    {
        return exception is MicrosoftAccountNameChangeException nameChangeException
            ? nameChangeException.Reason switch
            {
                MicrosoftAccountNameChangeFailureReason.DuplicateName => Strings.Status_AccountRenameFailedDuplicateName,
                MicrosoftAccountNameChangeFailureReason.NotAllowed => Strings.Status_AccountRenameFailedNotAllowed,
                MicrosoftAccountNameChangeFailureReason.InvalidName => Strings.Status_AccountRenameFailedInvalidName,
                _ => Strings.Status_AccountRenameFailed
            }
            : Strings.Status_AccountRenameFailed;
    }

    private void ReportStatus(string message)
    {
        statusService.Report(message);
    }

}

