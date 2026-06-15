using Launcher.Domain.Models;
using Launcher.Infrastructure;
using Launcher.Infrastructure.Persistence;

namespace Launcher.Tests.Infrastructure.Persistence;

public sealed class SettingsServiceTests : TestTempDirectory
{
    private static string DefaultMinecraftDirectory =>
        Path.GetFullPath(new LauncherPathProvider().DefaultMinecraftDirectory);

    [Fact]
    public async Task SettingsServiceWritesAndLoadsDefaults()
    {
        var service = new JsonSettingsService(TempRoot);

        var settings = await service.LoadAsync();
        settings.OfflineUsername = "Steve";
        settings.DefaultMemoryMb = 6144;
        await service.SaveAsync(settings);

        var loaded = await service.LoadAsync();

        Assert.Equal("Steve", loaded.OfflineUsername);
        Assert.Equal(6144, loaded.DefaultMemoryMb);
        Assert.Equal(TempRoot, loaded.DataDirectory);
        Assert.Equal(DefaultMinecraftDirectory, loaded.MinecraftDirectory);
    }

    [Fact]
    public async Task SettingsServiceBackfillsMinecraftDirectory()
    {
        Directory.CreateDirectory(TempRoot);
        await File.WriteAllTextAsync(Path.Combine(TempRoot, "settings.json"), "{}");
        var service = new JsonSettingsService(TempRoot);

        var loaded = await service.LoadAsync();

        Assert.Equal(DefaultMinecraftDirectory, loaded.MinecraftDirectory);
    }

    [Fact]
    public async Task SettingsServiceUsesCurrentExecutableMinecraftDirectory()
    {
        Directory.CreateDirectory(TempRoot);
        var staleMinecraftDirectory = Path.Combine(TempRoot, "old-debug", ".minecraft");
        await File.WriteAllTextAsync(
            Path.Combine(TempRoot, "settings.json"),
            $$"""
            {
              "MinecraftDirectory": "{{staleMinecraftDirectory.Replace("\\", "\\\\")}}"
            }
            """);
        var service = new JsonSettingsService(TempRoot);

        var loaded = await service.LoadAsync();

        Assert.Equal(DefaultMinecraftDirectory, loaded.MinecraftDirectory);
    }

    [Fact]
    public async Task SettingsServicePersistsOfflineAccounts()
    {
        var service = new JsonSettingsService(TempRoot);
        var settings = await service.LoadAsync();
        settings.Accounts =
        [
            new LauncherAccountRecord
            {
                Id = "offline-alex",
                DisplayName = "Alex",
                IsOffline = true
            }
        ];

        await service.SaveAsync(settings);
        var loaded = await service.LoadAsync();

        var account = Assert.Single(loaded.Accounts);
        Assert.True(loaded.AccountsInitialized);
        Assert.Equal("offline-alex", account.Id);
        Assert.Equal("Alex", account.DisplayName);
        Assert.True(account.IsOffline);
    }

    [Fact]
    public async Task SettingsServiceKeepsInitializedEmptyAccountList()
    {
        var service = new JsonSettingsService(TempRoot);
        var settings = await service.LoadAsync();
        settings.AccountsInitialized = true;
        settings.Accounts.Clear();

        await service.SaveAsync(settings);
        var loaded = await service.LoadAsync();

        Assert.True(loaded.AccountsInitialized);
        Assert.Empty(loaded.Accounts);
    }

    [Fact]
    public async Task SettingsServicePreservesMixedAccountOrder()
    {
        var service = new JsonSettingsService(TempRoot);
        var settings = await service.LoadAsync();
        settings.AccountsInitialized = true;
        settings.Accounts =
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

        await service.SaveAsync(settings);
        var loaded = await service.LoadAsync();

        Assert.Equal(
            ["offline-first", "microsoft-alex", "offline-last"],
            loaded.Accounts.Select(account => account.Id));
    }

    [Fact]
    public async Task SettingsServicePersistsCachedAccountCapes()
    {
        var service = new JsonSettingsService(TempRoot);
        var settings = await service.LoadAsync();
        settings.AccountsInitialized = true;
        settings.Accounts =
        [
            new LauncherAccountRecord
            {
                Id = "microsoft-alex",
                DisplayName = "Alex",
                Uuid = "alexuuid",
                IsOffline = false,
                Capes =
                [
                    new LauncherCapeRecord
                    {
                        Id = "cape-one",
                        DisplayName = "Cape One",
                        IsActive = true
                    }
                ]
            }
        ];

        await service.SaveAsync(settings);
        var loaded = await service.LoadAsync();

        var account = Assert.Single(loaded.Accounts);
        var cape = Assert.Single(account.Capes);
        Assert.Equal("cape-one", cape.Id);
        Assert.Equal("Cape One", cape.DisplayName);
        Assert.True(cape.IsActive);
    }
}

