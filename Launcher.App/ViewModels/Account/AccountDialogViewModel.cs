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
        IOfflineAccountUuidService offlineUuidService,
        IStatusService statusService,
        ILogger<AccountDialogViewModel>? logger = null)
    {
        this.accountList = accountList;
        this.microsoftAccountService = microsoftAccountService;
        this.offlineUuidService = offlineUuidService;
        this.statusService = statusService;
        this.logger = logger ?? NullLogger<AccountDialogViewModel>.Instance;
    }

    public ObservableCollection<AccountTypeOption> AccountTypeOptions { get; } = new(AccountTypeOptionFactory.Create());

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

    public bool IsRenameAccountInputStep => RenameAccountDialogStep == AccountDialogSteps.RenameInput;
    public bool IsRenameAccountStatusStep => RenameAccountDialogStep == AccountDialogSteps.RenameStatus;
    public bool IsRenameAccountResultStep => RenameAccountDialogStep == AccountDialogSteps.RenameResult;
    public bool IsRenameAccountMessageStep => IsRenameAccountStatusStep || IsRenameAccountResultStep;
    public bool IsRenameMicrosoftAccount => AccountPendingRename is not null && !AccountPendingRename.IsOffline;
    public bool CanShowRenameAccountCancelButton => !IsRenameAccountDialogBusy && IsRenameAccountInputStep;
    public bool CanConfirmRenameAccountDialog => !IsRenameAccountDialogBusy
        && (IsRenameAccountResultStep || (IsRenameAccountInputStep && !string.IsNullOrWhiteSpace(RenameAccountName)));
    public bool HasRenameAccountErrorCode => !string.IsNullOrWhiteSpace(RenameAccountErrorCodeMessage);

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
                !item.IsOffline && string.Equals(item.Uuid, account.Uuid, StringComparison.OrdinalIgnoreCase));

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

        // “确认”按钮在不同步骤含义不同：第一页只负责推进状态，真正新增发生在名称页。
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
            IsOffline = true
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
            if (!account.IsOffline)
                await microsoftAccountService.DeleteAccountAsync(account);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Account delete cleanup failed. AccountId={AccountId} IsOffline={IsOffline}", account.Id, account.IsOffline);
            ReportStatus(string.Format(Strings.Status_AccountDeletedCacheCleanupFailedFormat, deletedName));
        }
    }

    public void OpenRenameAccountDialog()
    {
        if (accountList.SelectedAccount is null)
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

