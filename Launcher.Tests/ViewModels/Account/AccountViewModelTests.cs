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
using Launcher.App.Services;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;
using Launcher.Tests.Fakes;

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
        var replacement = new LauncherAccount { Id = "first", DisplayName = "Updated", Kind = LauncherAccountKind.Offline };

        Assert.True(viewModel.TryReplaceAccount("first", replacement));

        Assert.Same(replacement, viewModel.SelectedAccount);
        Assert.Equal("Updated", viewModel.Accounts[0].DisplayName);
        Assert.True(viewModel.Accounts[0].IsSelected);
    }

    [Fact]
    public void SwitchingAccountCancelsPreviousProfileOperation()
    {
        using var operations = new AccountAppearanceOperationCoordinator();
        var first = CreateAccount("first");
        var second = CreateAccount("second");
        operations.SetAccount(first);
        var firstOperation = operations.Begin(first);

        operations.SetAccount(second);

        Assert.True(firstOperation.Token.IsCancellationRequested);
        Assert.False(operations.IsCurrent(first, firstOperation));
        Assert.False(operations.IsBusy);
    }

    [Fact]
    public async Task SilentRefreshUpdatesMicrosoftAndThirdPartyAccountsOnly()
    {
        var microsoft = new LauncherAccount
        {
            Id = "microsoft",
            DisplayName = "Microsoft",
            Kind = LauncherAccountKind.Microsoft
        };
        var thirdParty = new LauncherAccount
        {
            Id = "third-party",
            DisplayName = "Third Party",
            Kind = LauncherAccountKind.ThirdParty,
            AuthenticationServerUrl = "https://auth.example.test/api/yggdrasil/",
            Uuid = "00112233-4455-6677-8899-aabbccddeeff"
        };
        var offline = CreateAccount("offline");
        var store = new RecordingAccountStore(new AccountStoreSnapshot([microsoft, thirdParty, offline], thirdParty.Id));
        var accountList = new AccountListViewModel(store);
        await accountList.InitializeAsync(new LauncherSettings());
        var microsoftService = new UnusedMicrosoftAccountService();
        var thirdPartyService = new UnusedThirdPartyAccountService();
        using var operations = new AccountAppearanceOperationCoordinator();
        var profile = new AccountProfileViewModel(
            accountList,
            microsoftService,
            thirdPartyService,
            operations,
            null,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        await profile.RefreshAccountsSilentlyAsync();

        Assert.Equal(["microsoft"], microsoftService.RefreshedAccountIds);
        Assert.Equal(["third-party"], thirdPartyService.RefreshedAccountIds);
        Assert.Equal("file:///microsoft-avatar.png", accountList.FindAccount("microsoft")?.AvatarSource);
        Assert.Equal("file:///third-party-avatar.png", accountList.FindAccount("third-party")?.AvatarSource);
        Assert.Equal("Third Party Renamed", accountList.FindAccount("third-party")?.DisplayName);
        Assert.Null(accountList.FindAccount("offline")?.AvatarSource);
        Assert.Equal(3, store.SavedAccounts.Count);
    }

    [Fact]
    public async Task ThirdPartyAccountOptionOpensCredentialsStepWithoutEnablingConfirmation()
    {
        var viewModel = CreateDialogViewModel();
        var option = Assert.Single(
            viewModel.AccountTypeOptions,
            item => item.Kind == AccountTypeKinds.ThirdParty);

        Assert.Equal("account_page/third_party_login", option.IconKey);

        viewModel.OpenAddAccountDialog();
        viewModel.SelectedAccountTypeOption = option;
        Assert.True(viewModel.CanConfirmAddAccountDialog);

        await viewModel.ConfirmAddAccountDialogAsync();

        Assert.True(viewModel.IsThirdPartyCredentialsStep);
        Assert.True(viewModel.CanShowAddAccountBackButton);
        Assert.True(viewModel.CanShowAddAccountCancelButton);
        Assert.False(viewModel.CanConfirmAddAccountDialog);
    }

    [Fact]
    public async Task ThirdPartyCredentialsAreClearedWhenDialogIsReopened()
    {
        var viewModel = CreateDialogViewModel();
        viewModel.OpenAddAccountDialog();
        viewModel.SelectedAccountTypeOption = Assert.Single(
            viewModel.AccountTypeOptions,
            item => item.Kind == AccountTypeKinds.ThirdParty);
        await viewModel.ConfirmAddAccountDialogAsync();
        viewModel.ThirdParty.AuthenticationServer = "https://example.test/api/yggdrasil";
        viewModel.ThirdParty.UsernameOrEmail = "player@example.test";

        viewModel.BackToAddAccountTypeStep();

        Assert.True(viewModel.IsAccountTypeStep);
        Assert.False(viewModel.CanShowAddAccountBackButton);

        viewModel.OpenAddAccountDialog();

        Assert.Empty(viewModel.ThirdParty.AuthenticationServer);
        Assert.Empty(viewModel.ThirdParty.UsernameOrEmail);
    }

    [Fact]
    public async Task ThirdPartyLoginAddsAccountAndClosesDialog()
    {
        var service = new StubThirdPartyAccountService(new LauncherAccount
        {
            Id = "third-party-id",
            DisplayName = "Player",
            Kind = LauncherAccountKind.ThirdParty,
            Uuid = "00112233-4455-6677-8899-aabbccddeeff"
        });
        var viewModel = CreateDialogViewModel(service);
        viewModel.OpenAddAccountDialog();
        viewModel.SelectedAccountTypeOption = Assert.Single(
            viewModel.AccountTypeOptions,
            item => item.Kind == AccountTypeKinds.ThirdParty);
        await viewModel.ConfirmAddAccountDialogAsync();
        viewModel.ThirdParty.AuthenticationServer = "https://example.test/api/yggdrasil";
        viewModel.ThirdParty.UsernameOrEmail = "Player";
        viewModel.ThirdParty.UpdatePasswordState(true);

        Assert.True(viewModel.CanConfirmAddAccountDialog);
        await viewModel.ConfirmAddAccountDialogAsync("password");

        Assert.False(viewModel.IsAddAccountDialogOpen);
        Assert.Equal("password", service.ReceivedPassword);
    }

    [Fact]
    public async Task InvalidThirdPartyCredentialsShowPasswordErrorWithoutClosingDialog()
    {
        var service = new StubThirdPartyAccountService(
            ThirdPartyAccountLoginFailureReason.InvalidCredentials);
        var viewModel = CreateDialogViewModel(service);
        viewModel.OpenAddAccountDialog();
        viewModel.SelectedAccountTypeOption = Assert.Single(
            viewModel.AccountTypeOptions,
            item => item.Kind == AccountTypeKinds.ThirdParty);
        await viewModel.ConfirmAddAccountDialogAsync();
        viewModel.ThirdParty.AuthenticationServer = "https://example.test/api/yggdrasil";
        viewModel.ThirdParty.UsernameOrEmail = "Player";
        viewModel.ThirdParty.UpdatePasswordState(true);

        await viewModel.ConfirmAddAccountDialogAsync("wrong");

        Assert.True(viewModel.IsAddAccountDialogOpen);
        Assert.True(viewModel.ThirdParty.HasPasswordError);
        Assert.Equal(Launcher.App.Resources.Strings.Account_ThirdPartyInvalidCredentials, viewModel.ThirdParty.PasswordError);
    }

    [Fact]
    public async Task ThirdPartyReauthenticationLocksIdentityAndReplacesExistingAccount()
    {
        var existing = new LauncherAccount
        {
            Id = "third-party-id",
            DisplayName = "OldName",
            Kind = LauncherAccountKind.ThirdParty,
            Uuid = "00112233-4455-6677-8899-aabbccddeeff",
            AuthenticationServerUrl = "https://example.test/api/yggdrasil/",
            ThirdPartyLoginUsername = "login-name"
        };
        var refreshed = new LauncherAccount
        {
            Id = existing.Id,
            DisplayName = "NewName",
            Kind = LauncherAccountKind.ThirdParty,
            Uuid = existing.Uuid,
            AuthenticationServerUrl = existing.AuthenticationServerUrl,
            ThirdPartyLoginUsername = existing.ThirdPartyLoginUsername
        };
        var store = new RecordingAccountStore(new AccountStoreSnapshot([existing], existing.Id));
        var list = new AccountListViewModel(store);
        await list.InitializeAsync(new LauncherSettings());
        var service = new StubThirdPartyAccountService(refreshed);
        var viewModel = new AccountDialogViewModel(
            list,
            new UnusedMicrosoftAccountService(),
            service,
            new FakeOfflineAccountUuidService(),
            new NullStatusService());

        viewModel.OpenThirdPartyReauthenticationDialog(existing);

        Assert.True(viewModel.IsThirdPartyReauthenticationStep);
        Assert.True(viewModel.IsThirdPartyIdentityReadOnly);
        Assert.False(viewModel.CanShowAddAccountBackButton);
        Assert.Equal(existing.AuthenticationServerUrl, viewModel.ThirdParty.AuthenticationServer);
        Assert.Equal(existing.ThirdPartyLoginUsername, viewModel.ThirdParty.UsernameOrEmail);
        Assert.False(viewModel.CanConfirmAddAccountDialog);

        viewModel.ThirdParty.UpdatePasswordState(true);
        await viewModel.ConfirmAddAccountDialogAsync("password");

        Assert.False(viewModel.IsAddAccountDialogOpen);
        Assert.Equal("password", service.ReceivedPassword);
        Assert.Equal("NewName", list.SelectedAccount?.DisplayName);
        Assert.Equal(existing.Id, store.SelectedAccountId);
    }

    [Fact]
    public async Task MicrosoftReauthenticationReplacesSelectedAccountAndClosesDialog()
    {
        var existing = new LauncherAccount
        {
            Id = "microsoft-uuid",
            DisplayName = "OldName",
            Kind = LauncherAccountKind.Microsoft,
            Uuid = "uuid"
        };
        var refreshed = new LauncherAccount
        {
            Id = existing.Id,
            DisplayName = "NewName",
            Kind = LauncherAccountKind.Microsoft,
            Uuid = existing.Uuid
        };
        var store = new RecordingAccountStore(new AccountStoreSnapshot([existing], existing.Id));
        var list = new AccountListViewModel(store);
        await list.InitializeAsync(new LauncherSettings());
        var viewModel = new AccountDialogViewModel(
            list,
            new ReauthenticatingMicrosoftAccountService(refreshed),
            new UnusedThirdPartyAccountService(),
            new FakeOfflineAccountUuidService(),
            new NullStatusService());

        viewModel.OpenMicrosoftReauthenticationDialog(existing);

        Assert.True(viewModel.IsMicrosoftReauthenticationStep);
        Assert.True(viewModel.IsAddAccountDialogBusy);

        Assert.True(await viewModel.CompleteMicrosoftAccountReauthenticationAsync());

        Assert.False(viewModel.IsAddAccountDialogOpen);
        Assert.Equal("NewName", list.SelectedAccount?.DisplayName);
        Assert.Equal(existing.Id, store.SelectedAccountId);
    }

    [Fact]
    public async Task EmailProfilesSupportMultipleSelectionAndImportSelectedInServerOrder()
    {
        var service = new EmailThirdPartyAccountService();
        var store = new RecordingAccountStore(new AccountStoreSnapshot([], null));
        var list = new AccountListViewModel(store);
        await list.InitializeAsync(new LauncherSettings());
        var viewModel = CreateDialogViewModel(list, service);
        await OpenThirdPartyCredentialsAsync(viewModel, "user@example.test");

        await viewModel.ConfirmAddAccountDialogAsync("password");

        Assert.True(viewModel.IsThirdPartyProfileSelectionStep);
        Assert.Equal(2, viewModel.ThirdParty.Profiles.Count);
        Assert.False(viewModel.CanConfirmAddAccountDialog);
        viewModel.ThirdParty.Profiles[0].IsSelected = true;
        viewModel.ThirdParty.Profiles[1].IsSelected = true;
        Assert.True(viewModel.CanConfirmAddAccountDialog);
        Assert.False(viewModel.CanSelectAllThirdPartyProfiles);

        await viewModel.ConfirmAddAccountDialogAsync("password");

        Assert.False(viewModel.IsAddAccountDialogOpen);
        Assert.Equal(["profile-1", "profile-2"], service.ImportedProfileIds);
        Assert.Equal("profile-1", list.SelectedAccount?.DisplayName);
    }

    [Fact]
    public async Task EmailProfileRetryOnlyProcessesFailedProfiles()
    {
        var service = new EmailThirdPartyAccountService(failSecondOnce: true);
        var list = new AccountListViewModel(new RecordingAccountStore(new AccountStoreSnapshot([], null)));
        await list.InitializeAsync(new LauncherSettings());
        var viewModel = CreateDialogViewModel(list, service);
        await OpenThirdPartyCredentialsAsync(viewModel, "user@example.test");
        await viewModel.ConfirmAddAccountDialogAsync("password");
        viewModel.SelectAllThirdPartyProfiles();

        await viewModel.ConfirmAddAccountDialogAsync("password");

        Assert.True(viewModel.IsThirdPartyImportResultStep);
        Assert.Equal(1, viewModel.ThirdPartyImportFailedCount);
        Assert.Single(list.Accounts);

        await viewModel.RetryThirdPartyProfileImportAsync("password");

        Assert.False(viewModel.IsAddAccountDialogOpen);
        Assert.Equal(["profile-1", "profile-2", "profile-2"], service.ImportedProfileIds);
        Assert.Equal(2, list.Accounts.Count);
    }

    private static async Task OpenThirdPartyCredentialsAsync(AccountDialogViewModel viewModel, string identifier)
    {
        viewModel.OpenAddAccountDialog();
        viewModel.SelectedAccountTypeOption = Assert.Single(
            viewModel.AccountTypeOptions,
            item => item.Kind == AccountTypeKinds.ThirdParty);
        await viewModel.ConfirmAddAccountDialogAsync();
        viewModel.ThirdParty.AuthenticationServer = "https://example.test";
        viewModel.ThirdParty.UsernameOrEmail = identifier;
        viewModel.ThirdParty.UpdatePasswordState(true);
    }

    private static AccountDialogViewModel CreateDialogViewModel(IThirdPartyAccountService? thirdPartyService = null)
    {
        var list = new AccountListViewModel(
            new RecordingAccountStore(new AccountStoreSnapshot([], null)));
        return new AccountDialogViewModel(
            list,
            new UnusedMicrosoftAccountService(),
            thirdPartyService ?? new UnusedThirdPartyAccountService(),
            new FakeOfflineAccountUuidService(),
            new NullStatusService());
    }

    private static AccountDialogViewModel CreateDialogViewModel(
        AccountListViewModel list,
        IThirdPartyAccountService thirdPartyService) => new(
            list,
            new UnusedMicrosoftAccountService(),
            thirdPartyService,
            new FakeOfflineAccountUuidService(),
            new NullStatusService());

    private static LauncherAccount CreateAccount(string id)
    {
        return new LauncherAccount { Id = id, DisplayName = id, Kind = LauncherAccountKind.Offline };
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

    private sealed class NullStatusService : IStatusService
    {
        public event Action<string>? MessageReported
        {
            add { }
            remove { }
        }

        public void Report(string message)
        {
        }
    }

    private sealed class UnusedMicrosoftAccountService : IMicrosoftAccountService
    {
        public List<string> RefreshedAccountIds { get; } = [];

        public Task<IReadOnlyList<LauncherAccount>> GetSavedAccountsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LauncherAccount>>([]);

        public Task<LauncherAccount> LoginInteractivelyAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LauncherAccount> ReauthenticateInteractivelyAsync(
            LauncherAccount account,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteAccountAsync(LauncherAccount account, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AccountCapeOption>> GetCapesAsync(
            LauncherAccount account,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<LauncherAccount> RefreshAccountProfileAsync(
            LauncherAccount account,
            CancellationToken cancellationToken = default)
        {
            RefreshedAccountIds.Add(account.Id);
            return Task.FromResult(AccountMapper.WithRefreshedAppearance(
                account,
                "file:///microsoft-avatar.png",
                null,
                null,
                [],
                null));
        }

        public Task<LauncherAccount> UploadSkinAsync(
            LauncherAccount account,
            string skinFilePath,
            MinecraftSkinModel skinModel,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SetActiveCapeAsync(
            LauncherAccount account,
            string? capeId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<LauncherAccount> ChangeNameAsync(
            LauncherAccount account,
            string newName,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ReauthenticatingMicrosoftAccountService(LauncherAccount refreshed) : IMicrosoftAccountService
    {
        public Task<LauncherAccount> ReauthenticateInteractivelyAsync(
            LauncherAccount account,
            CancellationToken cancellationToken = default) => Task.FromResult(refreshed);

        public Task<IReadOnlyList<LauncherAccount>> GetSavedAccountsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LauncherAccount>>([]);

        public Task<LauncherAccount> LoginInteractivelyAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAccountAsync(LauncherAccount account, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AccountCapeOption>> GetCapesAsync(LauncherAccount account, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LauncherAccount> RefreshAccountProfileAsync(LauncherAccount account, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LauncherAccount> UploadSkinAsync(LauncherAccount account, string skinFilePath, MinecraftSkinModel skinModel, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetActiveCapeAsync(LauncherAccount account, string? capeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LauncherAccount> ChangeNameAsync(LauncherAccount account, string newName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnusedThirdPartyAccountService : IThirdPartyAccountService
    {
        public Task<ThirdPartyEmailLoginSession> BeginEmailLoginAsync(string authenticationServer, string email, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LauncherAccount> ImportEmailProfileAsync(string attemptId, string profileUuid, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CancelEmailLoginAsync(string attemptId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public List<string> RefreshedAccountIds { get; } = [];

        public Task<LauncherAccount> LoginWithUsernameAsync(
            string authenticationServer,
            string username,
            string password,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<LauncherAccount> RefreshAccountProfileAsync(
            LauncherAccount account,
            CancellationToken cancellationToken = default)
        {
            RefreshedAccountIds.Add(account.Id);
            return Task.FromResult(AccountMapper.WithDisplayName(
                AccountMapper.WithRefreshedAppearance(
                    account,
                    "file:///third-party-avatar.png",
                    null,
                    null,
                    [],
                    null),
                "Third Party Renamed"));
        }

        public Task<LauncherAccount> ReauthenticateAsync(
            LauncherAccount account,
            string password,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteCredentialsAsync(string accountId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubThirdPartyAccountService : IThirdPartyAccountService
    {
        public Task<ThirdPartyEmailLoginSession> BeginEmailLoginAsync(string authenticationServer, string email, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LauncherAccount> ImportEmailProfileAsync(string attemptId, string profileUuid, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CancelEmailLoginAsync(string attemptId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        private readonly LauncherAccount? account;
        private readonly ThirdPartyAccountLoginFailureReason? failureReason;

        public StubThirdPartyAccountService(LauncherAccount account)
        {
            this.account = account;
        }

        public StubThirdPartyAccountService(ThirdPartyAccountLoginFailureReason failureReason)
        {
            this.failureReason = failureReason;
        }

        public string? ReceivedPassword { get; private set; }

        public Task<LauncherAccount> LoginWithUsernameAsync(
            string authenticationServer,
            string username,
            string password,
            CancellationToken cancellationToken = default)
        {
            ReceivedPassword = password;
            if (failureReason is { } reason)
                throw new ThirdPartyAccountLoginException(reason, "failed");
            return Task.FromResult(account!);
        }

        public Task<LauncherAccount> RefreshAccountProfileAsync(
            LauncherAccount account,
            CancellationToken cancellationToken = default) => Task.FromResult(account);

        public Task<LauncherAccount> ReauthenticateAsync(
            LauncherAccount existingAccount,
            string password,
            CancellationToken cancellationToken = default) => LoginWithUsernameAsync(
                existingAccount.AuthenticationServerUrl!,
                existingAccount.ThirdPartyLoginUsername!,
                password,
                cancellationToken);

        public Task DeleteCredentialsAsync(string accountId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class EmailThirdPartyAccountService(bool failSecondOnce = false) : IThirdPartyAccountService
    {
        private bool secondFailed;
        public List<string> ImportedProfileIds { get; } = [];

        public Task<ThirdPartyEmailLoginSession> BeginEmailLoginAsync(string authenticationServer, string email, string password, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ThirdPartyEmailLoginSession("attempt", [
                new ThirdPartyProfileOption("00112233-4455-6677-8899-aabbccddeeff", "profile-1", LauncherAccount.DefaultSteveAvatarUrl),
                new ThirdPartyProfileOption("11112222-3333-4444-5555-666677778888", "profile-2", LauncherAccount.DefaultSteveAvatarUrl)]));

        public Task<LauncherAccount> ImportEmailProfileAsync(string attemptId, string profileUuid, string password, CancellationToken cancellationToken = default)
        {
            var isSecond = profileUuid.StartsWith("1111", StringComparison.Ordinal);
            ImportedProfileIds.Add(isSecond ? "profile-2" : "profile-1");
            if (isSecond && failSecondOnce && !secondFailed)
            {
                secondFailed = true;
                throw new ThirdPartyAccountLoginException(ThirdPartyAccountLoginFailureReason.ServerUnavailable, "failed");
            }
            return Task.FromResult(new LauncherAccount
            {
                Id = isSecond ? "account-2" : "account-1",
                DisplayName = isSecond ? "profile-2" : "profile-1",
                Kind = LauncherAccountKind.ThirdParty,
                Uuid = profileUuid,
                AuthenticationServerUrl = "https://example.test/",
                ThirdPartyLoginUsername = "user@example.test"
            });
        }

        public Task CancelEmailLoginAsync(string attemptId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LauncherAccount> LoginWithUsernameAsync(string authenticationServer, string username, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LauncherAccount> RefreshAccountProfileAsync(LauncherAccount account, CancellationToken cancellationToken = default) => Task.FromResult(account);
        public Task<LauncherAccount> ReauthenticateAsync(LauncherAccount account, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteCredentialsAsync(string accountId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
