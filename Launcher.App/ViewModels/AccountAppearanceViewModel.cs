using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Application.Accounts;
using Launcher.App.Resources;
using Launcher.App.Services;

namespace Launcher.App.ViewModels;

public sealed partial class AccountAppearanceViewModel : ObservableObject
{
    private readonly AccountListViewModel accountList;
    private readonly IMicrosoftAccountService microsoftAccountService;
    private readonly IAccountDialogService dialogService;
    private readonly IFilePickerService filePickerService;
    private readonly IMinecraftSkinFileValidator skinFileValidator;

    [ObservableProperty]
    private AccountCapeOption? selectedAccountCapeOption;

    [ObservableProperty]
    private bool isAccountProfileBusy;

    [ObservableProperty]
    private string accountProfileMessage = string.Empty;

    [ObservableProperty]
    private string accountProfileErrorCodeMessage = string.Empty;

    public AccountAppearanceViewModel(
        AccountListViewModel accountList,
        IMicrosoftAccountService microsoftAccountService,
        AccountSkinModelDialogViewModel skinModelDialog,
        IAccountDialogService dialogService,
        IFilePickerService filePickerService,
        IMinecraftSkinFileValidator skinFileValidator)
    {
        this.accountList = accountList;
        this.microsoftAccountService = microsoftAccountService;
        SkinModelDialog = skinModelDialog;
        this.dialogService = dialogService;
        this.filePickerService = filePickerService;
        this.skinFileValidator = skinFileValidator;
        this.accountList.PropertyChanged += AccountList_PropertyChanged;
    }

    public ObservableCollection<AccountCapeOption> SelectedAccountCapeOptions { get; } = [];

    public AccountSkinModelDialogViewModel SkinModelDialog { get; }

    public bool CanChangeSelectedAccountSkin => accountList.SelectedAccount is not null
        && !accountList.SelectedAccount.IsOffline;

    public bool CanEditSelectedMicrosoftAccount => accountList.SelectedAccount is not null
        && !accountList.SelectedAccount.IsOffline
        && !IsAccountProfileBusy;

    public bool CanApplySelectedCape => accountList.SelectedAccount is not null
        && !accountList.SelectedAccount.IsOffline
        && SelectedAccountCapeOption is not null;

    public bool HasSelectedAccountCapes => SelectedAccountCapeOptions.Count > 0;

    public bool HasAccountProfileErrorCode => !string.IsNullOrWhiteSpace(AccountProfileErrorCodeMessage);

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

    public async Task ChangeSelectedAccountSkinAsync(string skinFilePath, MinecraftSkinModel skinModel)
    {
        var account = accountList.SelectedAccount;
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
            AccountProfileErrorCodeMessage = string.Empty;
            AccountProfileMessage = Strings.Status_UploadingSkin;
            var updatedAccount = await microsoftAccountService.UploadSkinAsync(account, skinFilePath, skinModel);
            accountList.ReplaceSelectedAccount(account, updatedAccount);
            await accountList.PersistAccountOrderAsync();
            AccountProfileMessage = Strings.Status_SkinUpdated;
            await LoadSelectedAccountProfileAsync(updatedAccount);
        }
        catch (Exception ex)
        {
            AccountProfileMessage = Strings.Status_SkinUpdateFailed;
            AccountProfileErrorCodeMessage = GetErrorCodeMessage(ex);
        }
        finally
        {
            IsAccountProfileBusy = false;
        }
    }

    [RelayCommand]
    public async Task PickAndChangeSelectedAccountSkinAsync()
    {
        if (!CanChangeSelectedAccountSkin)
            return;

        var skinFilePath = filePickerService.PickMinecraftSkin();
        if (string.IsNullOrWhiteSpace(skinFilePath))
            return;

        var validation = await skinFileValidator.ValidateAsync(skinFilePath);
        if (validation.IsValid)
            dialogService.ShowSkinModelDialog(skinFilePath);
        else
            dialogService.ShowSkinFormatErrorDialog();
    }

    [RelayCommand]
    public void RequestCancelSkinModelDialog()
    {
        dialogService.CancelSkinModelDialog();
    }

    [RelayCommand]
    public Task RequestConfirmSkinModelDialogAsync()
    {
        return dialogService.ConfirmSkinModelDialogAsync();
    }

    [RelayCommand]
    public async Task RefreshSelectedAccountProfileAsync()
    {
        if (accountList.SelectedAccount is not null)
            await LoadSelectedAccountProfileAsync(accountList.SelectedAccount);
    }

    [RelayCommand]
    public async Task RefreshSelectedAccountInfoAsync()
    {
        var account = accountList.SelectedAccount;
        if (account is null)
            return;

        if (account.IsOffline)
        {
            AccountProfileMessage = Strings.Status_AccountProfileRefreshOfflineUnsupported;
            return;
        }

        try
        {
            IsAccountProfileBusy = true;
            AccountProfileErrorCodeMessage = string.Empty;
            AccountProfileMessage = Strings.Status_RefreshingAccountProfile;
            var refreshedAccount = await microsoftAccountService.RefreshAccountProfileAsync(account);
            accountList.ReplaceSelectedAccount(
                account,
                AccountMapper.WithCapeCache(refreshedAccount, account.CachedCapeOptions));
            await accountList.PersistAccountOrderAsync();
            AccountProfileMessage = Strings.Status_AccountProfileRefreshed;
        }
        catch (Exception ex)
        {
            AccountProfileMessage = Strings.Status_AccountProfileRefreshFailed;
            AccountProfileErrorCodeMessage = GetErrorCodeMessage(ex);
        }
        finally
        {
            IsAccountProfileBusy = false;
        }
    }

    public async Task RefreshMicrosoftAccountsSilentlyAsync()
    {
        var accounts = accountList.Accounts
            .Where(account => !account.IsOffline)
            .ToList();
        var hasChanges = false;

        foreach (var account in accounts)
        {
            LauncherAccount refreshedAccount;
            try
            {
                refreshedAccount = await microsoftAccountService.RefreshAccountProfileAsync(account);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                continue;
            }

            var currentAccount = accountList.FindAccount(account.Id);
            if (currentAccount is null)
                continue;

            if (IsAccountProfileBusy && IsSelectedAccount(currentAccount))
                continue;

            var updatedAccount = AccountMapper.WithCapeCache(
                refreshedAccount,
                currentAccount.CachedCapeOptions);
            hasChanges |= accountList.TryReplaceAccount(currentAccount.Id, updatedAccount);
        }

        if (!hasChanges)
            return;

        try
        {
            await accountList.PersistAccountOrderAsync();
        }
        catch
        {
        }
    }

    public async Task RefreshCurrentSecondaryContentAsync()
    {
        if (accountList.SelectedAccount is null || IsAccountProfileBusy)
            return;

        await RefreshSelectedAccountProfileAsync();
    }

    [RelayCommand]
    public async Task ApplySelectedAccountCapeAsync()
    {
        var account = accountList.SelectedAccount;
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
            AccountProfileErrorCodeMessage = string.Empty;
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
            AccountProfileErrorCodeMessage = string.Empty;
        }
        finally
        {
            IsAccountProfileBusy = false;
        }
    }

    partial void OnSelectedAccountCapeOptionChanged(AccountCapeOption? value)
    {
        OnPropertyChanged(nameof(CanApplySelectedCape));
    }

    partial void OnIsAccountProfileBusyChanged(bool value)
    {
        NotifySelectedAccountProfileActionPropertiesChanged();
    }

    partial void OnAccountProfileErrorCodeMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasAccountProfileErrorCode));
    }

    private async Task LoadSelectedAccountProfileAsync(LauncherAccount account)
    {
        SelectedAccountCapeOptions.Clear();
        SelectedAccountCapeOption = null;
        OnPropertyChanged(nameof(HasSelectedAccountCapes));
        OnPropertyChanged(nameof(CanApplySelectedCape));

        if (!IsSelectedAccount(account))
            return;

        if (account.IsOffline)
        {
            AccountProfileMessage = Strings.Account_ProfileOfflineUnsupported;
            return;
        }

        try
        {
            IsAccountProfileBusy = true;
            AccountProfileErrorCodeMessage = string.Empty;
            AccountProfileMessage = Strings.Status_LoadingAccountProfile;
            var capes = await microsoftAccountService.GetCapesAsync(account);
            if (!IsSelectedAccount(account))
                return;

            PopulateSelectedAccountCapeOptions(capes);
            await StoreSelectedAccountCapeCacheAsync();
            OnPropertyChanged(nameof(CanApplySelectedCape));
            AccountProfileMessage = SelectedAccountCapeOptions.Count == 0
                ? Strings.Account_ProfileNoCapes
                : Strings.Account_ProfileLoaded;
        }
        catch (Exception ex)
        {
            if (IsSelectedAccount(account))
            {
                AccountProfileMessage = Strings.Status_LoadAccountProfileFailed;
                AccountProfileErrorCodeMessage = GetErrorCodeMessage(ex);
            }
        }
        finally
        {
            if (IsSelectedAccount(account))
                IsAccountProfileBusy = false;
        }
    }

    private bool IsSelectedAccount(LauncherAccount account)
    {
        var selectedAccount = accountList.SelectedAccount;
        return ReferenceEquals(selectedAccount, account)
            || (selectedAccount is not null
                && string.Equals(selectedAccount.Id, account.Id, StringComparison.Ordinal));
    }

    private void ResetSelectedAccountProfileState(LauncherAccount? account)
    {
        SelectedAccountCapeOptions.Clear();
        SelectedAccountCapeOption = null;

        if (account is null)
        {
            AccountProfileMessage = string.Empty;
            AccountProfileErrorCodeMessage = string.Empty;
            NotifyAccountSelectionPropertiesChanged();
            return;
        }

        PopulateSelectedAccountCapeOptions(account.CachedCapeOptions);
        AccountProfileErrorCodeMessage = string.Empty;
        AccountProfileMessage = account.IsOffline
            ? Strings.Account_ProfileOfflineUnsupported
            : SelectedAccountCapeOptions.Count > 0
                ? Strings.Account_ProfileCacheLoaded
                : Strings.Account_ProfileRefreshHint;
        NotifyAccountSelectionPropertiesChanged();
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
        var account = accountList.SelectedAccount;
        if (account is null)
            return;

        accountList.ReplaceSelectedAccount(
            account,
            AccountMapper.WithCapeCache(account, SelectedAccountCapeOptions.ToList()));
        await accountList.PersistAccountOrderAsync();
    }

    private void AccountList_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountListViewModel.SelectedAccount))
            ResetSelectedAccountProfileState(accountList.SelectedAccount);
    }

    private void NotifyAccountSelectionPropertiesChanged()
    {
        OnPropertyChanged(nameof(CanChangeSelectedAccountSkin));
        NotifySelectedAccountProfileActionPropertiesChanged();
    }

    private void NotifySelectedAccountProfileActionPropertiesChanged()
    {
        OnPropertyChanged(nameof(CanEditSelectedMicrosoftAccount));
        OnPropertyChanged(nameof(CanApplySelectedCape));
    }

    private static string GetErrorCodeMessage(Exception exception)
    {
        var errorCode = exception switch
        {
            MicrosoftAccountSkinUpdateException { ErrorCode: { Length: > 0 } code } => code,
            MicrosoftAccountProfileRefreshException { ErrorCode: { Length: > 0 } code } => code,
            _ => null
        };

        return string.IsNullOrWhiteSpace(errorCode)
            ? string.Empty
            : string.Format(Strings.Status_ErrorCodeFormat, errorCode);
    }
}
