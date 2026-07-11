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
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.ViewModels.Account;

public sealed class AccountCapeViewModelTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ThirdPartyAccountShowsOnlyCurrentCapeOrNone(bool hasCape)
    {
        var capes = hasCape
            ? new List<AccountCapeOption>
            {
                new() { DisplayName = string.Empty, IsNone = true },
                new()
                {
                    Id = "current-cape",
                    DisplayName = string.Empty,
                    ImageUrl = "file:///cape.png",
                    IsActive = true
                }
            }
            : [];
        var account = new LauncherAccount
        {
            Id = "third-party",
            DisplayName = "Player",
            Kind = LauncherAccountKind.ThirdParty,
            CachedCapeOptions = capes
        };
        var store = new RecordingAccountStore(new AccountStoreSnapshot([account], account.Id));
        var accountList = new AccountListViewModel(store);
        await accountList.InitializeAsync(new LauncherSettings());
        var microsoftService = new RecordingMicrosoftAccountService();
        using var operations = new AccountAppearanceOperationCoordinator();
        var profile = new AccountProfileViewModel(
            accountList,
            microsoftService,
            new UnusedThirdPartyAccountService(),
            operations,
            null,
            NullLogger.Instance);
        profile.SetAccount(accountList.SelectedAccount);
        var viewModel = new AccountCapeViewModel(accountList, microsoftService, profile, NullLogger.Instance);

        viewModel.SetAccount(accountList.SelectedAccount);

        var selected = Assert.Single(viewModel.Options);
        Assert.Equal(!hasCape, selected.IsNone);
        Assert.True(selected.IsActive);
        Assert.Null(viewModel.PreviousOption);
        Assert.Null(viewModel.NextOption);
        Assert.True(viewModel.HasPreview);
        Assert.True(viewModel.CanRefresh);
        Assert.False(viewModel.CanApply);
        Assert.False(viewModel.CanShowApplyButton);
    }

    [Theory]
    [InlineData("cape-two")]
    [InlineData(null)]
    public async Task ApplyPreservesOptionsAndPersistsActiveCape(string? capeId)
    {
        var account = new LauncherAccount
        {
            Id = "account",
            DisplayName = "Player",
            Kind = LauncherAccountKind.Microsoft,
            CachedCapeOptions =
            [
                new AccountCapeOption { DisplayName = string.Empty, IsNone = true },
                new AccountCapeOption { Id = "cape-one", DisplayName = "Cape One", IsActive = true },
                new AccountCapeOption { Id = "cape-two", DisplayName = "Cape Two" }
            ]
        };
        var store = new RecordingAccountStore(new AccountStoreSnapshot([account], account.Id));
        var accountList = new AccountListViewModel(store);
        await accountList.InitializeAsync(new LauncherSettings());
        var service = new RecordingMicrosoftAccountService();
        using var operations = new AccountAppearanceOperationCoordinator();
        var profile = new AccountProfileViewModel(
            accountList,
            service,
            new UnusedThirdPartyAccountService(),
            operations,
            null,
            NullLogger.Instance);
        profile.SetAccount(accountList.SelectedAccount);
        var viewModel = new AccountCapeViewModel(accountList, service, profile, NullLogger.Instance);
        viewModel.SetAccount(accountList.SelectedAccount);
        viewModel.SelectedOption = viewModel.Options.Single(option =>
            capeId is null ? option.IsNone : string.Equals(option.Id, capeId, StringComparison.Ordinal));

        await viewModel.ApplyAsync();

        Assert.Equal(1, service.SetActiveCapeCallCount);
        Assert.Equal(capeId, service.LastCapeId);
        Assert.Equal(3, viewModel.Options.Count);
        Assert.Equal([null, "cape-one", "cape-two"], viewModel.Options.Select(option => option.Id));
        AssertActiveCape(viewModel.Options, capeId);
        Assert.NotNull(viewModel.SelectedOption);
        Assert.True(viewModel.SelectedOption.IsActive);
        Assert.Equal(capeId, viewModel.SelectedOption.Id);

        var cachedOptions = Assert.IsAssignableFrom<IReadOnlyList<AccountCapeOption>>(
            accountList.SelectedAccount?.CachedCapeOptions);
        Assert.Equal(3, cachedOptions.Count);
        AssertActiveCape(cachedOptions, capeId);

        var persistedAccount = Assert.Single(store.SavedAccounts);
        Assert.Equal(3, persistedAccount.CachedCapeOptions.Count);
        AssertActiveCape(persistedAccount.CachedCapeOptions, capeId);
    }

    private static void AssertActiveCape(IEnumerable<AccountCapeOption> options, string? capeId)
    {
        var active = Assert.Single(options, option => option.IsActive);
        Assert.Equal(capeId, active.Id);
        Assert.Equal(capeId is null, active.IsNone);
    }

    private sealed class RecordingAccountStore(AccountStoreSnapshot snapshot) : IAccountStore
    {
        public IReadOnlyList<LauncherAccount> SavedAccounts { get; private set; } = [];

        public Task<AccountStoreSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);

        public Task SaveOrderAsync(
            string? selectedAccountId,
            IEnumerable<LauncherAccount> accounts,
            CancellationToken cancellationToken = default)
        {
            SavedAccounts = accounts.ToArray();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingMicrosoftAccountService : IMicrosoftAccountService
    {
        public int SetActiveCapeCallCount { get; private set; }
        public string? LastCapeId { get; private set; }

        public Task<IReadOnlyList<LauncherAccount>> GetSavedAccountsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LauncherAccount> LoginInteractivelyAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LauncherAccount> ReauthenticateInteractivelyAsync(
            LauncherAccount account,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteAccountAsync(LauncherAccount account, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AccountCapeOption>> GetCapesAsync(
            LauncherAccount account,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LauncherAccount> RefreshAccountProfileAsync(
            LauncherAccount account,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LauncherAccount> UploadSkinAsync(
            LauncherAccount account,
            string skinFilePath,
            MinecraftSkinModel skinModel,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetActiveCapeAsync(
            LauncherAccount account,
            string? capeId,
            CancellationToken cancellationToken = default)
        {
            SetActiveCapeCallCount++;
            LastCapeId = capeId;
            return Task.CompletedTask;
        }

        public Task<LauncherAccount> ChangeNameAsync(
            LauncherAccount account,
            string newName,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedThirdPartyAccountService : IThirdPartyAccountService
    {
        public Task<ThirdPartyEmailLoginSession> BeginEmailLoginAsync(string authenticationServer, string email, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LauncherAccount> ImportEmailProfileAsync(string attemptId, string profileUuid, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CancelEmailLoginAsync(string attemptId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LauncherAccount> LoginWithUsernameAsync(
            string authenticationServer,
            string username,
            string password,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<LauncherAccount> RefreshAccountProfileAsync(
            LauncherAccount account,
            CancellationToken cancellationToken = default) => Task.FromResult(account);

        public Task<LauncherAccount> ReauthenticateAsync(
            LauncherAccount account,
            string password,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteCredentialsAsync(string accountId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
