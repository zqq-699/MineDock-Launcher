using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Application.Accounts;
using Launcher.App.Resources;

namespace Launcher.App.ViewModels;

public sealed partial class AccountAppearanceViewModel : ObservableObject
{
    private readonly AccountListViewModel accountList;
    private readonly IMicrosoftAccountService microsoftAccountService;

    [ObservableProperty]
    private AccountCapeOption? selectedAccountCapeOption;

    [ObservableProperty]
    private bool isAccountProfileBusy;

    [ObservableProperty]
    private string accountProfileMessage = string.Empty;

    public AccountAppearanceViewModel(
        AccountListViewModel accountList,
        IMicrosoftAccountService microsoftAccountService)
    {
        this.accountList = accountList;
        this.microsoftAccountService = microsoftAccountService;
        this.accountList.PropertyChanged += AccountList_PropertyChanged;
    }

    public ObservableCollection<AccountCapeOption> SelectedAccountCapeOptions { get; } = [];

    public bool CanChangeSelectedAccountSkin => accountList.SelectedAccount is not null
        && !accountList.SelectedAccount.IsOffline;

    public bool CanEditSelectedMicrosoftAccount => accountList.SelectedAccount is not null
        && !accountList.SelectedAccount.IsOffline
        && !IsAccountProfileBusy;

    public bool CanApplySelectedCape => accountList.SelectedAccount is not null
        && !accountList.SelectedAccount.IsOffline
        && SelectedAccountCapeOption is not null;

    public bool HasSelectedAccountCapes => SelectedAccountCapeOptions.Count > 0;

    public async Task ChangeSelectedAccountSkinAsync(string skinFilePath)
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
            AccountProfileMessage = Strings.Status_UploadingSkin;
            var updatedAccount = await microsoftAccountService.UploadSkinAsync(account, skinFilePath);
            accountList.ReplaceSelectedAccount(account, updatedAccount);
            await accountList.PersistAccountOrderAsync();
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

    public async Task RefreshSelectedAccountProfileAsync()
    {
        if (accountList.SelectedAccount is not null)
            await LoadSelectedAccountProfileAsync(accountList.SelectedAccount);
    }

    public async Task RefreshCurrentSecondaryContentAsync()
    {
        if (accountList.SelectedAccount is null || IsAccountProfileBusy)
            return;

        await RefreshSelectedAccountProfileAsync();
    }

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

    partial void OnSelectedAccountCapeOptionChanged(AccountCapeOption? value)
    {
        OnPropertyChanged(nameof(CanApplySelectedCape));
    }

    partial void OnIsAccountProfileBusyChanged(bool value)
    {
        NotifySelectedAccountProfileActionPropertiesChanged();
    }

    private async Task LoadSelectedAccountProfileAsync(LauncherAccount account)
    {
        SelectedAccountCapeOptions.Clear();
        SelectedAccountCapeOption = null;
        OnPropertyChanged(nameof(HasSelectedAccountCapes));
        OnPropertyChanged(nameof(CanApplySelectedCape));

        if (!ReferenceEquals(accountList.SelectedAccount, account))
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
            if (!ReferenceEquals(accountList.SelectedAccount, account))
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
            if (ReferenceEquals(accountList.SelectedAccount, account))
                AccountProfileMessage = Strings.Status_LoadAccountProfileFailed;
        }
        finally
        {
            if (ReferenceEquals(accountList.SelectedAccount, account))
                IsAccountProfileBusy = false;
        }
    }

    private void ResetSelectedAccountProfileState(LauncherAccount? account)
    {
        SelectedAccountCapeOptions.Clear();
        SelectedAccountCapeOption = null;

        if (account is null)
        {
            AccountProfileMessage = string.Empty;
            NotifyAccountSelectionPropertiesChanged();
            return;
        }

        PopulateSelectedAccountCapeOptions(account.CachedCapeOptions);
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
}
