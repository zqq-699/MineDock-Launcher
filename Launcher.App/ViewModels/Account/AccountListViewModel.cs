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
    private LauncherAccount? selectedAccount;

    public AccountListViewModel(IAccountStore accountStore)
    {
        this.accountStore = accountStore;
    }

    public ObservableCollection<LauncherAccount> Accounts { get; } = [];

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
        var cachedAccounts = launcherSettings.Accounts
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

        selectedAccountId = account.Id;
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
        selectedAccountId = newAccount.Id;
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
            selectedAccountId = newAccount.Id;
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
        selectedAccountId = null;
        UpdateSelectionFlags();
    }

    public Task PersistAccountOrderAsync()
    {
        selectedAccountId = SelectedAccount?.Id;
        return accountStore.SaveOrderAsync(selectedAccountId, Accounts);
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
            string.Equals(account.Id, selectedAccountId, StringComparison.Ordinal));
        if (rememberedAccount is not null)
            SelectAccount(rememberedAccount, persistSelection: false);
        else
            ClearSelectedAccount();
    }
}

