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

using Launcher.App.ViewModels.Account;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.Tests.ViewModels.Account;

public sealed class AccountViewModelTests
{
    [Fact]
    public async Task SelectionStateLivesOnWrapperAndPersistsModelOrder()
    {
        var first = CreateAccount("first");
        var second = CreateAccount("second");
        var store = new RecordingAccountStore(new AccountStoreSnapshot([first, second], "second"));
        var viewModel = new AccountListViewModel(store);

        await viewModel.InitializeAsync(new LauncherSettings());

        Assert.Equal("second", viewModel.SelectedAccount?.Id);
        Assert.False(viewModel.Accounts[0].IsSelected);
        Assert.True(viewModel.Accounts[1].IsSelected);
        viewModel.SelectAccount(viewModel.Accounts[0]);
        await viewModel.PersistAccountOrderAsync();
        Assert.Equal(["first", "second"], store.SavedAccounts.Select(account => account.Id));
        Assert.Equal("first", store.SelectedAccountId);
    }

    [Fact]
    public async Task ReplacingSelectedAccountPreservesPositionAndSelection()
    {
        var first = CreateAccount("first");
        var second = CreateAccount("second");
        var store = new RecordingAccountStore(new AccountStoreSnapshot([first, second], "first"));
        var viewModel = new AccountListViewModel(store);
        await viewModel.InitializeAsync(new LauncherSettings());
        var replacement = new LauncherAccount { Id = "first", DisplayName = "Updated", IsOffline = true };

        Assert.True(viewModel.TryReplaceAccount("first", replacement));

        Assert.Same(replacement, viewModel.SelectedAccount);
        Assert.Equal("Updated", viewModel.Accounts[0].DisplayName);
        Assert.True(viewModel.Accounts[0].IsSelected);
    }

    [Fact]
    public void SwitchingAccountCancelsPreviousProfileOperation()
    {
        using var profile = new AccountProfileViewModel();
        var first = CreateAccount("first");
        var second = CreateAccount("second");
        profile.SetAccount(first);
        var firstOperation = profile.BeginOperation(first.Id, "loading");

        profile.SetAccount(second);

        Assert.True(firstOperation.IsCancellationRequested);
        Assert.False(profile.IsCurrent(first, firstOperation));
        Assert.False(profile.IsBusy);
    }

    [Fact]
    public void SkinAndCapeChildrenOwnAdjacentSelection()
    {
        var skins = new AccountSkinLibraryViewModel();
        var firstSkin = new LauncherSkinRecord { Id = "skin-1" };
        var secondSkin = new LauncherSkinRecord { Id = "skin-2" };
        skins.Skins.Add(firstSkin);
        skins.Skins.Add(secondSkin);
        skins.SelectedSkin = firstSkin;
        var capes = new AccountCapeViewModel();
        var firstCape = new AccountCapeOption { Id = "cape-1", DisplayName = "Cape 1" };
        var secondCape = new AccountCapeOption { Id = "cape-2", DisplayName = "Cape 2" };
        capes.Options.Add(firstCape);
        capes.Options.Add(secondCape);
        capes.SelectedOption = secondCape;

        skins.SelectNext();
        capes.SelectPrevious();

        Assert.Same(secondSkin, skins.SelectedSkin);
        Assert.Same(firstCape, capes.SelectedOption);
    }

    private static LauncherAccount CreateAccount(string id)
    {
        return new LauncherAccount { Id = id, DisplayName = id, IsOffline = true };
    }

    private sealed class RecordingAccountStore(AccountStoreSnapshot snapshot) : IAccountStore
    {
        public string? SelectedAccountId { get; private set; }
        public IReadOnlyList<LauncherAccount> SavedAccounts { get; private set; } = [];

        public Task<AccountStoreSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);

        public Task SaveOrderAsync(
            string? selectedAccountId,
            IEnumerable<LauncherAccount> accounts,
            CancellationToken cancellationToken = default)
        {
            SelectedAccountId = selectedAccountId;
            SavedAccounts = accounts.ToArray();
            return Task.CompletedTask;
        }
    }
}
