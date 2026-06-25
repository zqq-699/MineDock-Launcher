using Launcher.Domain.Models;
using Launcher.Infrastructure.Persistence;

namespace Launcher.Tests.Infrastructure.Persistence;

public sealed class JsonAccountStateServiceTests : TestTempDirectory
{
    [Fact]
    public async Task AccountStateServiceWritesAndLoadsDefaults()
    {
        var service = new JsonAccountStateService(accountDataDirectory: TempRoot);

        var state = await service.LoadAsync();

        Assert.True(state.AccountsInitialized);
        Assert.False(state.MicrosoftAccountsImported);
        Assert.Equal(LauncherDefaults.DefaultOfflineUsername, state.OfflineUsername);
        Assert.Empty(state.Accounts);
        Assert.True(File.Exists(Path.Combine(TempRoot, "account-state.json")));
    }

    [Fact]
    public async Task AccountStateServicePreservesMixedAccountOrderAndSelectedAccount()
    {
        var service = new JsonAccountStateService(accountDataDirectory: TempRoot);
        var state = await service.LoadAsync();
        state.SelectedAccountId = "microsoft-alex";
        state.MicrosoftAccountsImported = true;
        state.Accounts =
        [
            new LauncherAccountRecord
            {
                Id = "offline-first",
                DisplayName = "First",
                IsOffline = true
            },
            new LauncherAccountRecord
            {
                Id = "microsoft-alex",
                DisplayName = "Alex",
                Uuid = "alexuuid",
                IsOffline = false
            },
            new LauncherAccountRecord
            {
                Id = "offline-last",
                DisplayName = "Last",
                IsOffline = true
            }
        ];

        await service.SaveAsync(state);
        var loaded = await service.LoadAsync();

        Assert.Equal("microsoft-alex", loaded.SelectedAccountId);
        Assert.True(loaded.MicrosoftAccountsImported);
        Assert.Equal(
            ["offline-first", "microsoft-alex", "offline-last"],
            loaded.Accounts.Select(account => account.Id));
    }

    [Fact]
    public async Task AccountStateServiceDropsInvalidSelectedAccountAndInvalidNestedRecords()
    {
        var service = new JsonAccountStateService(accountDataDirectory: TempRoot);
        var state = await service.LoadAsync();
        state.SelectedAccountId = "missing";
        state.Accounts =
        [
            new LauncherAccountRecord
            {
                Id = "microsoft-alex",
                DisplayName = "Alex",
                Uuid = "alexuuid",
                IsOffline = false,
                ActiveSkinId = "missing-skin",
                Skins =
                [
                    new LauncherSkinRecord(),
                    new LauncherSkinRecord
                    {
                        Id = "skin-one",
                        Source = "skin.png",
                        ContentHash = "hash",
                        AddedAtUtc = DateTimeOffset.UnixEpoch
                    }
                ],
                Capes =
                [
                    new LauncherCapeRecord(),
                    new LauncherCapeRecord
                    {
                        Id = "cape-one",
                        DisplayName = "Cape One",
                        IsActive = true
                    }
                ]
            }
        ];

        await service.SaveAsync(state);
        var loaded = await service.LoadAsync();

        var account = Assert.Single(loaded.Accounts);
        Assert.Null(loaded.SelectedAccountId);
        Assert.Null(account.ActiveSkinId);
        Assert.Single(account.Skins);
        Assert.Single(account.Capes);
    }
}
