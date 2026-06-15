using Launcher.Application.Accounts;
using Launcher.Domain.Models;
using Launcher.Application.Services;

namespace Launcher.Tests;

public sealed class AccountStoreTests
{
    [Fact]
    public async Task LoadAsync_PreservesStoredOrderAndImportsMicrosoftAccountsOnce()
    {
        var settings = new LauncherSettings
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

        var settingsService = new FakeSettingsService();
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
        var store = new AccountStore(settingsService, microsoftService, new FakeOfflineAccountUuidService());

        var accounts = await store.LoadAsync(settings);

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
        Assert.True(settings.MicrosoftAccountsImported);
        Assert.Equal(1, settingsService.SaveCount);
    }

    [Fact]
    public async Task SaveOrderAsync_WritesAccountRecordsAndFirstOfflineUsername()
    {
        var settings = new LauncherSettings();
        var settingsService = new FakeSettingsService();
        var store = new AccountStore(
            settingsService,
            new FakeMicrosoftAccountService(),
            new FakeOfflineAccountUuidService());
        var accounts = new[]
        {
            new LauncherAccount
            {
                Id = "ms-1",
                DisplayName = "Microsoft",
                Uuid = "uuid",
                IsOffline = false
            },
            new LauncherAccount
            {
                Id = "offline-1",
                DisplayName = "Offline",
                IsOffline = true
            }
        };

        await store.SaveOrderAsync(settings, accounts);

        Assert.True(settings.AccountsInitialized);
        Assert.True(settings.MicrosoftAccountsImported);
        Assert.Equal("Offline", settings.OfflineUsername);
        Assert.Collection(
            settings.Accounts,
            account => Assert.Equal("ms-1", account.Id),
            account =>
            {
                Assert.Equal("offline-1", account.Id);
                Assert.Equal("Standard-Offline", account.Uuid);
                Assert.Equal(OfflineUuidGenerationMode.Standard, account.OfflineUuidGenerationMode);
            });
        Assert.Same(settings, settingsService.LastSavedSettings);
    }

    [Fact]
    public async Task LoadAsync_PreservesRandomOfflineUuid()
    {
        var settings = new LauncherSettings
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
        var store = new AccountStore(
            new FakeSettingsService(),
            new FakeMicrosoftAccountService(),
            new FakeOfflineAccountUuidService());

        var accounts = await store.LoadAsync(settings);

        var account = Assert.Single(accounts);
        Assert.Equal("existing-random", account.Uuid);
        Assert.Equal(OfflineUuidGenerationMode.Random, account.OfflineUuidGenerationMode);
    }

    [Fact]
    public async Task LoadAsync_PersistsMicrosoftAvatarMergedFromCache()
    {
        var settings = new LauncherSettings
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
        var settingsService = new FakeSettingsService();
        var store = new AccountStore(
            settingsService,
            new FakeMicrosoftAccountService(
                new LauncherAccount
                {
                    Id = "ms-1",
                    DisplayName = "LiveName",
                    Uuid = "uuid",
                    AvatarSource = "cached-avatar.png",
                    HasFreshProfile = true,
                    IsOffline = false
                }),
            new FakeOfflineAccountUuidService());

        var accounts = await store.LoadAsync(settings);

        var account = Assert.Single(accounts);
        Assert.Equal("LiveName", account.DisplayName);
        Assert.Equal("cached-avatar.png", account.AvatarSource);
        Assert.Equal(1, settingsService.SaveCount);
        var savedAccount = Assert.Single(settings.Accounts);
        Assert.Equal("LiveName", savedAccount.DisplayName);
        Assert.Equal("cached-avatar.png", savedAccount.AvatarSource);
    }

    [Fact]
    public async Task LoadAsync_KeepsStoredMicrosoftNameWhenRefreshFallsBackToCache()
    {
        var settings = new LauncherSettings
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
        var store = new AccountStore(
            new FakeSettingsService(),
            new FakeMicrosoftAccountService(
                new LauncherAccount
                {
                    Id = "ms-1",
                    DisplayName = "CachedName",
                    Uuid = "uuid",
                    IsOffline = false
                }),
            new FakeOfflineAccountUuidService());

        var accounts = await store.LoadAsync(settings);

        var account = Assert.Single(accounts);
        Assert.Equal("StoredName", account.DisplayName);
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public int SaveCount { get; private set; }

        public LauncherSettings? LastSavedSettings { get; private set; }

        public Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LauncherSettings());
        }

        public Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            LastSavedSettings = settings;
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
