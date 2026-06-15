using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels;

public sealed partial class AccountListViewModel : ObservableObject
{
    private readonly IAccountStore accountStore;
    private LauncherSettings settings = new();

    [ObservableProperty]
    private LauncherAccount? selectedAccount;

    public AccountListViewModel(IAccountStore accountStore)
    {
        this.accountStore = accountStore;
    }

    public ObservableCollection<LauncherAccount> Accounts { get; } = [];

    public async Task InitializeAsync(LauncherSettings launcherSettings)
    {
        settings = launcherSettings;
        var loadedAccounts = await accountStore.LoadAsync(settings);
        ApplyAccounts(loadedAccounts);
    }

    public void PrimeFromSettings(LauncherSettings launcherSettings)
    {
        settings = launcherSettings;
        var cachedAccounts = settings.Accounts
            .Select(AccountMapper.FromRecord)
            .ToList();
        ApplyAccounts(cachedAccounts);
    }

    [RelayCommand]
    public void SelectAccount(LauncherAccount account)
    {
        SelectAccount(account, persistSelection: true);
    }

    public void SelectAccount(LauncherAccount account, bool persistSelection)
    {
        SelectedAccount = account;
        UpdateSelectionFlags();

        settings.SelectedAccountId = account.Id;
        if (persistSelection)
            _ = PersistAccountOrderAsync();
    }

    public async Task AddAndSelectAsync(LauncherAccount account)
    {
        Accounts.Add(account);
        SelectAccount(account, persistSelection: false);
        await PersistAccountOrderAsync();
    }

    public async Task RemoveAsync(LauncherAccount account)
    {
        if (ReferenceEquals(SelectedAccount, account))
            ClearSelectedAccount();

        Accounts.Remove(account);
        await PersistAccountOrderAsync();
    }

    public void ReplaceSelectedAccount(LauncherAccount oldAccount, LauncherAccount newAccount)
    {
        if (TryReplaceAccount(oldAccount.Id, newAccount))
            return;

        SelectedAccount = newAccount;
        settings.SelectedAccountId = newAccount.Id;
        UpdateSelectionFlags();
    }

    public async Task ReplaceSelectedAccountAndPersistAsync(LauncherAccount oldAccount, LauncherAccount newAccount)
    {
        ReplaceSelectedAccount(oldAccount, newAccount);
        await PersistAccountOrderAsync();
    }

    public bool TryReplaceAccount(string accountId, LauncherAccount newAccount)
    {
        var index = -1;
        for (var i = 0; i < Accounts.Count; i++)
        {
            if (string.Equals(Accounts[i].Id, accountId, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
            return false;

        var isSelectedAccount = SelectedAccount is not null
            && string.Equals(SelectedAccount.Id, accountId, StringComparison.Ordinal);
        Accounts[index] = newAccount;
        if (isSelectedAccount)
        {
            SelectedAccount = newAccount;
            settings.SelectedAccountId = newAccount.Id;
        }

        UpdateSelectionFlags();
        return true;
    }

    public LauncherAccount? FindAccount(string accountId)
    {
        return Accounts.FirstOrDefault(account =>
            string.Equals(account.Id, accountId, StringComparison.Ordinal));
    }

    public void ClearSelectedAccount()
    {
        SelectedAccount = null;
        settings.SelectedAccountId = null;
        UpdateSelectionFlags();
    }

    public Task PersistAccountOrderAsync()
    {
        settings.SelectedAccountId = SelectedAccount?.Id;
        return accountStore.SaveOrderAsync(settings, Accounts);
    }

    private void UpdateSelectionFlags()
    {
        foreach (var item in Accounts)
            item.IsSelected = ReferenceEquals(item, SelectedAccount);
    }

    private void ApplyAccounts(IEnumerable<LauncherAccount> accounts)
    {
        Accounts.Clear();
        foreach (var account in accounts)
            Accounts.Add(account);

        var rememberedAccount = Accounts.FirstOrDefault(account =>
            string.Equals(account.Id, settings.SelectedAccountId, StringComparison.Ordinal));
        if (rememberedAccount is not null)
            SelectAccount(rememberedAccount, persistSelection: false);
        else
            ClearSelectedAccount();
    }
}
