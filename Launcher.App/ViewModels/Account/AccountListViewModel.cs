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
using CommunityToolkit.Mvvm.Input;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Account;

public sealed partial class AccountListViewModel : ObservableObject
{
    private readonly IAccountStore accountStore;
    private string? selectedAccountId;

    [ObservableProperty]
    private AccountItemViewModel? selectedItem;

    public AccountListViewModel(IAccountStore accountStore)
    {
        this.accountStore = accountStore;
    }

    public ObservableCollection<AccountItemViewModel> Accounts { get; } = [];

    public LauncherAccount? SelectedAccount => SelectedItem?.Account;

    public async Task InitializeAsync(LauncherSettings launcherSettings)
    {
        var snapshot = await accountStore.LoadAsync();
        selectedAccountId = snapshot.SelectedAccountId;
        ApplyAccounts(snapshot.Accounts);
    }

    public void PrimeFromSettings(LauncherSettings launcherSettings)
    {
        if (launcherSettings.Accounts.Count == 0 && string.IsNullOrWhiteSpace(launcherSettings.SelectedAccountId))
            return;
        selectedAccountId = launcherSettings.SelectedAccountId;
        ApplyAccounts(launcherSettings.Accounts.Select(AccountMapper.FromRecord));
    }

    [RelayCommand]
    public void SelectAccount(AccountItemViewModel item)
    {
        SelectItem(item, persistSelection: true);
    }

    public void SelectAccount(LauncherAccount account)
    {
        SelectAccount(account, persistSelection: true);
    }

    public void SelectAccount(LauncherAccount account, bool persistSelection)
    {
        var item = Accounts.FirstOrDefault(candidate => string.Equals(candidate.Id, account.Id, StringComparison.Ordinal));
        if (item is not null)
            SelectItem(item, persistSelection);
    }

    public void SelectItem(AccountItemViewModel item, bool persistSelection)
    {
        SelectedItem = item;
        selectedAccountId = item.Id;
        if (persistSelection)
            _ = PersistAccountOrderAsync();
    }

    public async Task AddAndSelectAsync(LauncherAccount account)
    {
        var item = new AccountItemViewModel(account);
        Accounts.Add(item);
        SelectItem(item, persistSelection: false);
        await PersistAccountOrderAsync();
    }

    public async Task RemoveAsync(LauncherAccount account)
    {
        var item = Accounts.FirstOrDefault(candidate => string.Equals(candidate.Id, account.Id, StringComparison.Ordinal));
        if (item is null)
            return;
        if (ReferenceEquals(SelectedItem, item))
            ClearSelectedAccount();
        Accounts.Remove(item);
        await PersistAccountOrderAsync();
    }

    public void ReplaceSelectedAccount(LauncherAccount oldAccount, LauncherAccount newAccount)
    {
        if (TryReplaceAccount(oldAccount.Id, newAccount))
            return;
        var item = new AccountItemViewModel(newAccount);
        Accounts.Add(item);
        SelectItem(item, persistSelection: false);
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

        var wasSelected = ReferenceEquals(SelectedItem, Accounts[index]);
        var replacement = new AccountItemViewModel(newAccount);
        Accounts[index] = replacement;
        if (wasSelected)
        {
            SelectedItem = replacement;
            selectedAccountId = newAccount.Id;
        }
        UpdateSelectionFlags();
        return true;
    }

    public LauncherAccount? FindAccount(string accountId)
    {
        return Accounts.FirstOrDefault(item => string.Equals(item.Id, accountId, StringComparison.Ordinal))?.Account;
    }

    public void ClearSelectedAccount()
    {
        SelectedItem = null;
        selectedAccountId = null;
    }

    public Task PersistAccountOrderAsync()
    {
        selectedAccountId = SelectedItem?.Id;
        return accountStore.SaveOrderAsync(selectedAccountId, Accounts.Select(item => item.Account).ToArray());
    }

    partial void OnSelectedItemChanged(AccountItemViewModel? value)
    {
        UpdateSelectionFlags();
        OnPropertyChanged(nameof(SelectedAccount));
    }

    private void UpdateSelectionFlags()
    {
        foreach (var item in Accounts)
            item.IsSelected = ReferenceEquals(item, SelectedItem);
    }

    private void ApplyAccounts(IEnumerable<LauncherAccount> accounts)
    {
        Accounts.Clear();
        foreach (var account in accounts)
            Accounts.Add(new AccountItemViewModel(account));
        var remembered = Accounts.FirstOrDefault(item => string.Equals(item.Id, selectedAccountId, StringComparison.Ordinal));
        if (remembered is not null)
            SelectItem(remembered, persistSelection: false);
        else
            ClearSelectedAccount();
    }
}
