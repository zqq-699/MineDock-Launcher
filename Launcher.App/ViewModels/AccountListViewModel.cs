using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
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

    public void SelectAccount(LauncherAccount account, bool persistSelection)
    {
        SelectedAccount = account;
        foreach (var item in Accounts)
            item.IsSelected = ReferenceEquals(item, account);

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
        var index = Accounts.IndexOf(oldAccount);
        if (index >= 0)
            Accounts[index] = newAccount;

        SelectedAccount = newAccount;
        foreach (var item in Accounts)
            item.IsSelected = ReferenceEquals(item, newAccount);

        settings.SelectedAccountId = newAccount.Id;
    }

    public async Task ReplaceSelectedAccountAndPersistAsync(LauncherAccount oldAccount, LauncherAccount newAccount)
    {
        ReplaceSelectedAccount(oldAccount, newAccount);
        await PersistAccountOrderAsync();
    }

    public void ClearSelectedAccount()
    {
        SelectedAccount = null;
        settings.SelectedAccountId = null;
        foreach (var item in Accounts)
            item.IsSelected = false;
    }

    public Task PersistAccountOrderAsync()
    {
        settings.SelectedAccountId = SelectedAccount?.Id;
        return accountStore.SaveOrderAsync(settings, Accounts);
    }
}
