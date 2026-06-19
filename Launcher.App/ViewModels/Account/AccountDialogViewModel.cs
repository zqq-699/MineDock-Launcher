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

public sealed partial class AccountDialogViewModel : ObservableObject
{
    private const string DialogBusyIcon = "\uE895";
    private const string DialogSuccessIcon = "\uE73E";
    private const string DialogFailureIcon = "\uE783";
    private const string RenameInputIcon = "\uE70F";

    private static string MicrosoftLoginInitialMessage => Strings.Status_OpeningMicrosoftLogin;
    private static string MicrosoftLoginActiveMessage => Strings.Status_LoginMicrosoftActive;

    private readonly AccountListViewModel accountList;
    private readonly IMicrosoftAccountService microsoftAccountService;
    private readonly IOfflineAccountUuidService offlineUuidService;
    private readonly IStatusService statusService;
    private readonly ILogger<AccountDialogViewModel> logger;

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

            var existing = accountList.Accounts.FirstOrDefault(item =>
                !item.IsOffline && string.Equals(item.Uuid, account.Uuid, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                accountList.SelectAccount(existing, persistSelection: false);
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
        if (!AccountNameValidator.IsValid(accountName))
        {
            IsNewOfflineAccountNameInvalid = true;
            ReportStatus(AccountNameValidator.ValidationMessage);
            return;
        }

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
            IsRenameAccountDialogBusy = true;
            RenameAccountDialogStep = AccountDialogSteps.RenameStatus;
            RenameAccountIcon = DialogBusyIcon;
            RenameAccountMessage = account.IsOffline
                ? Strings.Status_SavingOfflineAccountName
                : Strings.Status_ChangingMicrosoftAccountName;

            LauncherAccount updatedAccount;
            if (account.IsOffline)
            {
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

