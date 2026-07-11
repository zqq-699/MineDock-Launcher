/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Accounts;
using Launcher.Application.Services;

namespace Launcher.Tests.Accounts;

public sealed class AccountStoreTests
{
    [Fact]
    public async Task LoadPreservesOrderAndImportsMicrosoftAccountsOnce()
    {
        var state = new FakeStateService(new LauncherAccountState
        {
            Accounts =
            [
                new() { Id = "offline", DisplayName = "Local", IsOffline = true },
                new() { Id = "ms-1", DisplayName = "Stored", Uuid = "old", IsOffline = false }
            ]
        });
        var store = new AccountStore(state, new FakeMicrosoftService(
            new LauncherAccount { Id = "ms-1", DisplayName = "Live", Uuid = "new", Kind = LauncherAccountKind.Microsoft, HasFreshProfile = true },
            new LauncherAccount { Id = "ms-2", DisplayName = "Imported", Uuid = "uuid", Kind = LauncherAccountKind.Microsoft }),
            new FakeOfflineAccountUuidService());

        var accounts = (await store.LoadAsync()).Accounts;

        Assert.Equal(["offline", "ms-1", "ms-2"], accounts.Select(account => account.Id));
        Assert.Equal("Live", accounts[1].DisplayName);
        Assert.True(state.State.MicrosoftAccountsImported);
        Assert.Equal(1, state.SaveCount);
    }

    [Fact]
    public async Task LoadKeepsStoredProfileFieldsWhenRefreshFallsBackToCache()
    {
        var state = new FakeStateService(new LauncherAccountState
        {
            MicrosoftAccountsImported = true,
            Accounts = [new()
            {
                Id = "ms-1",
                DisplayName = "Stored",
                Uuid = "uuid",
                SkinSource = "stored.png",
                SkinModel = MinecraftSkinModel.Classic
            }]
        });
        var store = new AccountStore(state,
            new FakeMicrosoftService(new LauncherAccount { Id = "ms-1", DisplayName = "Cached", Uuid = "uuid", Kind = LauncherAccountKind.Microsoft }),
            new FakeOfflineAccountUuidService());

        var account = Assert.Single((await store.LoadAsync()).Accounts);

        Assert.Equal("Stored", account.DisplayName);
        Assert.Equal("stored.png", account.SkinSource);
        Assert.Equal(MinecraftSkinModel.Classic, account.SkinModel);
    }

    [Fact]
    public async Task LoadPreservesThirdPartyRecordsWithoutMicrosoftReconciliation()
    {
        var state = new FakeStateService(new LauncherAccountState
        {
            MicrosoftAccountsImported = true,
            Accounts = [new LauncherAccountRecord
            {
                Id = "third-party-id",
                DisplayName = "Player",
                Kind = LauncherAccountKind.ThirdParty,
                IsOffline = false,
                Uuid = "00112233-4455-6677-8899-aabbccddeeff",
                AuthenticationServerUrl = "https://example.test/api/yggdrasil/",
                ThirdPartyLoginUsername = "player"
            }]
        });
        var store = new AccountStore(state, new FakeMicrosoftService(), new FakeOfflineAccountUuidService());

        var account = Assert.Single((await store.LoadAsync()).Accounts);

        Assert.True(account.IsThirdParty);
        Assert.Equal("https://example.test/api/yggdrasil/", account.AuthenticationServerUrl);
        Assert.Equal("player", account.ThirdPartyLoginUsername);
        Assert.Equal(0, state.SaveCount);
    }

    [Fact]
    public void MapperMigratesLegacyNonOfflineRecordToMicrosoft()
    {
        var account = AccountMapper.FromRecord(new LauncherAccountRecord
        {
            Id = "legacy",
            DisplayName = "Legacy",
            IsOffline = false
        });

        Assert.True(account.IsMicrosoft);
    }

    private sealed class FakeStateService(LauncherAccountState state) : IAccountStateService
    {
        public LauncherAccountState State { get; private set; } = state;
        public int SaveCount { get; private set; }
        public Task<LauncherAccountState> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(State);
        public Task SaveAsync(LauncherAccountState value, CancellationToken cancellationToken = default)
        { State = value; SaveCount++; return Task.CompletedTask; }
    }

    private sealed class FakeMicrosoftService(params LauncherAccount[] accounts) : IMicrosoftAccountService
    {
        public Task<IReadOnlyList<LauncherAccount>> GetSavedAccountsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LauncherAccount>>(accounts);
        public Task<LauncherAccount> LoginInteractivelyAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAccountAsync(LauncherAccount account, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AccountCapeOption>> GetCapesAsync(LauncherAccount account, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LauncherAccount> RefreshAccountProfileAsync(LauncherAccount account, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LauncherAccount> UploadSkinAsync(LauncherAccount account, string path, MinecraftSkinModel model,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetActiveCapeAsync(LauncherAccount account, string? capeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LauncherAccount> ChangeNameAsync(LauncherAccount account, string name,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
