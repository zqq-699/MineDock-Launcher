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

using Launcher.Application.Accounts;
using Launcher.Domain.Models;
using Launcher.Application.Services;

namespace Launcher.Tests.Accounts;

public sealed class AccountStoreTests
{
    [Fact]
    public async Task LoadAsync_PreservesStoredOrderAndImportsMicrosoftAccountsOnce()
    {
        var state = new LauncherAccountState
        {
            MicrosoftAccountsImported = false,
            Accounts =
            [
                new()
                {
                    Id = "offline-1",
                    DisplayName = "Local",
                    IsOffline = true
                },
                new()
                {
                    Id = "ms-1",
                    DisplayName = "StoredName",
                    Uuid = "stored-uuid",
                    IsOffline = false
                }
            ]
        };

        var accountStateService = new FakeAccountStateService(state);
        var microsoftService = new FakeMicrosoftAccountService(
            new LauncherAccount
            {
                Id = "ms-1",
                DisplayName = "LiveName",
                Uuid = "live-uuid",
                HasFreshProfile = true,
                IsOffline = false
            },
            new LauncherAccount
            {
                Id = "ms-2",
                DisplayName = "Imported",
                Uuid = "imported-uuid",
                IsOffline = false
            });
        var store = new AccountStore(accountStateService, microsoftService, new FakeOfflineAccountUuidService());

        var snapshot = await store.LoadAsync();
        var accounts = snapshot.Accounts;

        Assert.Collection(
            accounts,
            account =>
            {
                Assert.Equal("offline-1", account.Id);
                Assert.Equal("Local", account.DisplayName);
                Assert.Equal("Standard-Local", account.Uuid);
                Assert.Equal(OfflineUuidGenerationMode.Standard, account.OfflineUuidGenerationMode);
                Assert.True(account.IsOffline);
            },
            account =>
            {
                Assert.Equal("ms-1", account.Id);
                Assert.Equal("LiveName", account.DisplayName);
                Assert.Equal("live-uuid", account.Uuid);
                Assert.False(account.IsOffline);
            },
            account =>
            {
                Assert.Equal("ms-2", account.Id);
                Assert.Equal("Imported", account.DisplayName);
                Assert.False(account.IsOffline);
            });
        Assert.True(accountStateService.State.MicrosoftAccountsImported);
        Assert.Equal(1, accountStateService.SaveCount);
    }

    [Fact]
    public async Task SaveOrderAsync_WritesAccountRecordsAndFirstOfflineUsername()
    {
        var accountStateService = new FakeAccountStateService();
        var store = new AccountStore(
            accountStateService,
            new FakeMicrosoftAccountService(),
            new FakeOfflineAccountUuidService());
        var accounts = new[]
        {
            new LauncherAccount
            {
                Id = "ms-1",
                DisplayName = "Microsoft",
                Uuid = "uuid",
                SkinSource = "cached-skin.png",
                SkinModel = MinecraftSkinModel.Slim,
                ActiveSkinId = "skin-1",
                SkinLibrary =
                [
                    new LauncherSkinRecord
                    {
                        Id = "skin-1",
                        Source = "cached-skin.png",
                        SkinModel = MinecraftSkinModel.Slim,
                        ContentHash = "hash-1",
                        AddedAtUtc = DateTimeOffset.UnixEpoch
                    }
                ],
                IsOffline = false
            },
            new LauncherAccount
            {
                Id = "offline-1",
                DisplayName = "Offline",
                IsOffline = true
            }
        };

        await store.SaveOrderAsync("ms-1", accounts);

        var state = accountStateService.State;
        Assert.True(state.AccountsInitialized);
        Assert.True(state.MicrosoftAccountsImported);
        Assert.Equal("Offline", state.OfflineUsername);
        Assert.Equal("ms-1", state.SelectedAccountId);
        Assert.Collection(
            state.Accounts,
            account =>
            {
                Assert.Equal("ms-1", account.Id);
                Assert.Equal("cached-skin.png", account.SkinSource);
                Assert.Equal(MinecraftSkinModel.Slim, account.SkinModel);
                Assert.Equal("skin-1", account.ActiveSkinId);
                var skin = Assert.Single(account.Skins);
                Assert.Equal("cached-skin.png", skin.Source);
                Assert.Equal("hash-1", skin.ContentHash);
            },
            account =>
            {
                Assert.Equal("offline-1", account.Id);
                Assert.Equal("Standard-Offline", account.Uuid);
                Assert.Equal(OfflineUuidGenerationMode.Standard, account.OfflineUuidGenerationMode);
            });
        Assert.Same(state, accountStateService.LastSavedState);
    }

    [Fact]
    public async Task LoadAsync_PreservesRandomOfflineUuid()
    {
        var state = new LauncherAccountState
        {
            MicrosoftAccountsImported = true,
            Accounts =
            [
                new()
                {
                    Id = "offline-1",
                    DisplayName = "Local",
                    Uuid = "existing-random",
                    OfflineUuidGenerationMode = OfflineUuidGenerationMode.Random,
                    IsOffline = true
                }
            ]
        };
        var accountStateService = new FakeAccountStateService(state);
        var store = new AccountStore(
            accountStateService,
            new FakeMicrosoftAccountService(),
            new FakeOfflineAccountUuidService());

        var accounts = (await store.LoadAsync()).Accounts;

        var account = Assert.Single(accounts);
        Assert.Equal("existing-random", account.Uuid);
        Assert.Equal(OfflineUuidGenerationMode.Random, account.OfflineUuidGenerationMode);
    }

    [Fact]
    public async Task LoadAsync_PersistsMicrosoftAvatarMergedFromCache()
    {
        var state = new LauncherAccountState
        {
            MicrosoftAccountsImported = true,
            Accounts =
            [
                new()
                {
                    Id = "ms-1",
                    DisplayName = "StoredName",
                    Uuid = "uuid",
                    IsOffline = false
                }
            ]
        };
        var accountStateService = new FakeAccountStateService(state);
        var store = new AccountStore(
            accountStateService,
            new FakeMicrosoftAccountService(
                new LauncherAccount
                {
                    Id = "ms-1",
                    DisplayName = "LiveName",
                    Uuid = "uuid",
                    AvatarSource = "cached-avatar.png",
                    SkinSource = "cached-skin.png",
                    SkinModel = MinecraftSkinModel.Slim,
                    ActiveSkinId = "skin-1",
                    SkinLibrary =
                    [
                        new LauncherSkinRecord
                        {
                            Id = "skin-1",
                            Source = "cached-skin.png",
                            SkinModel = MinecraftSkinModel.Slim,
                            ContentHash = "hash-1",
                            AddedAtUtc = DateTimeOffset.UnixEpoch
                        }
                    ],
                    HasFreshProfile = true,
                    IsOffline = false
                }),
            new FakeOfflineAccountUuidService());

        var accounts = (await store.LoadAsync()).Accounts;

        var account = Assert.Single(accounts);
        Assert.Equal("LiveName", account.DisplayName);
        Assert.Equal("cached-avatar.png", account.AvatarSource);
        Assert.Equal("cached-skin.png", account.SkinSource);
        Assert.Equal(MinecraftSkinModel.Slim, account.SkinModel);
        Assert.Equal("skin-1", account.ActiveSkinId);
        Assert.Single(account.SkinLibrary);
        Assert.Equal(1, accountStateService.SaveCount);
        var savedAccount = Assert.Single(accountStateService.State.Accounts);
        Assert.Equal("LiveName", savedAccount.DisplayName);
        Assert.Equal("cached-avatar.png", savedAccount.AvatarSource);
        Assert.Equal("cached-skin.png", savedAccount.SkinSource);
        Assert.Equal(MinecraftSkinModel.Slim, savedAccount.SkinModel);
        Assert.Equal("skin-1", savedAccount.ActiveSkinId);
        Assert.Single(savedAccount.Skins);
    }

    [Fact]
    public async Task LoadAsync_KeepsStoredMicrosoftSkinWhenRefreshHasNoSkin()
    {
        var state = new LauncherAccountState
        {
            MicrosoftAccountsImported = true,
            Accounts =
            [
                new()
                {
                    Id = "ms-1",
                    DisplayName = "StoredName",
                    Uuid = "uuid",
                    SkinSource = "stored-skin.png",
                    SkinModel = MinecraftSkinModel.Classic,
                    IsOffline = false
                }
            ]
        };
        var accountStateService = new FakeAccountStateService(state);
        var store = new AccountStore(
            accountStateService,
            new FakeMicrosoftAccountService(
                new LauncherAccount
                {
                    Id = "ms-1",
                    DisplayName = "CachedName",
                    Uuid = "uuid",
                    IsOffline = false
                }),
            new FakeOfflineAccountUuidService());

        var accounts = (await store.LoadAsync()).Accounts;

        var account = Assert.Single(accounts);
        Assert.Equal("stored-skin.png", account.SkinSource);
        Assert.Equal(MinecraftSkinModel.Classic, account.SkinModel);
    }

    [Fact]
    public async Task LoadAsync_KeepsStoredMicrosoftNameWhenRefreshFallsBackToCache()
    {
        var state = new LauncherAccountState
        {
            MicrosoftAccountsImported = true,
            Accounts =
            [
                new()
                {
                    Id = "ms-1",
                    DisplayName = "StoredName",
                    Uuid = "uuid",
                    IsOffline = false
                }
            ]
        };
        var accountStateService = new FakeAccountStateService(state);
        var store = new AccountStore(
            accountStateService,
            new FakeMicrosoftAccountService(
                new LauncherAccount
                {
                    Id = "ms-1",
                    DisplayName = "CachedName",
                    Uuid = "uuid",
                    IsOffline = false
                }),
            new FakeOfflineAccountUuidService());

        var accounts = (await store.LoadAsync()).Accounts;

        var account = Assert.Single(accounts);
        Assert.Equal("StoredName", account.DisplayName);
    }

    private sealed class FakeAccountStateService : IAccountStateService
    {
        public FakeAccountStateService(LauncherAccountState? state = null)
        {
            State = state ?? new LauncherAccountState();
        }

        public LauncherAccountState State { get; private set; }

        public int SaveCount { get; private set; }

        public LauncherAccountState? LastSavedState { get; private set; }

        public Task<LauncherAccountState> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(State);
        }

        public Task SaveAsync(LauncherAccountState state, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            State = state;
            LastSavedState = state;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMicrosoftAccountService : IMicrosoftAccountService
    {
        private readonly IReadOnlyList<LauncherAccount> savedAccounts;

        public FakeMicrosoftAccountService(params LauncherAccount[] savedAccounts)
        {
            this.savedAccounts = savedAccounts;
        }

        public Task<IReadOnlyList<LauncherAccount>> GetSavedAccountsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(savedAccounts);
        }

        public Task<LauncherAccount> LoginInteractivelyAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task DeleteAccountAsync(LauncherAccount account, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<AccountCapeOption>> GetCapesAsync(LauncherAccount account, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<LauncherAccount> RefreshAccountProfileAsync(
            LauncherAccount account,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<LauncherAccount> UploadSkinAsync(
            LauncherAccount account,
            string skinFilePath,
            MinecraftSkinModel skinModel,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SetActiveCapeAsync(LauncherAccount account, string? capeId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<LauncherAccount> ChangeNameAsync(LauncherAccount account, string newName, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}

