using Launcher.App.Models;
using Launcher.App.Services;
using Launcher.Core.Models;
using Launcher.Core.Services;

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
                IsOffline = false
            },
            new LauncherAccount
            {
                Id = "ms-2",
                DisplayName = "Imported",
                Uuid = "imported-uuid",
                IsOffline = false
            });
        var store = new AccountStore(settingsService, microsoftService);

        var accounts = await store.LoadAsync(settings);

        Assert.Collection(
            accounts,
            account =>
            {
                Assert.Equal("offline-1", account.Id);
                Assert.Equal("Local", account.DisplayName);
                Assert.True(account.IsOffline);
            },
            account =>
            {
                Assert.Equal("ms-1", account.Id);
                Assert.Equal("StoredName", account.DisplayName);
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
        var store = new AccountStore(settingsService, new FakeMicrosoftAccountService());
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
            account => Assert.Equal("offline-1", account.Id));
        Assert.Same(settings, settingsService.LastSavedSettings);
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

        public Task<LauncherAccount> UploadSkinAsync(LauncherAccount account, string skinFilePath, CancellationToken cancellationToken = default)
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
