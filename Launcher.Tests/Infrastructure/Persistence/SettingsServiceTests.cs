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
        Assert.True(settings.AccountsInitialized);
        Assert.Empty(settings.Accounts);

        settings.OfflineUsername = "Steve";
        settings.DefaultMemorySettingsMode = MemorySettingsMode.Manual;
        settings.DefaultMemoryMb = 6144;
        await service.SaveAsync(settings);

        var loaded = await service.LoadAsync();

        Assert.Equal("Steve", loaded.OfflineUsername);
        Assert.Equal(MemorySettingsMode.Manual, loaded.DefaultMemorySettingsMode);
        Assert.Equal(6144, loaded.DefaultMemoryMb);
        Assert.Equal(TempRoot, loaded.DataDirectory);
        Assert.Equal(DefaultMinecraftDirectory, loaded.MinecraftDirectory);
        Assert.Equal(LauncherDefaults.DefaultTheme, loaded.Theme);
        Assert.True(loaded.ThemeFollowSystem);
        Assert.Empty(loaded.Accounts);
    }

    [Fact]
    public async Task SettingsServiceBackfillsInvalidTheme()
    {
        Directory.CreateDirectory(TempRoot);
        await File.WriteAllTextAsync(
            Path.Combine(TempRoot, "settings.json"),
            """
            {
              "Theme": "Blue"
            }
            """);
        var service = new JsonSettingsService(TempRoot);

        var loaded = await service.LoadAsync();

        Assert.Equal(LauncherDefaults.DefaultTheme, loaded.Theme);
        Assert.True(loaded.ThemeFollowSystem);
    }

    [Fact]
    public async Task SettingsServiceRoundTripsThemePreference()
    {
        var service = new JsonSettingsService(TempRoot);
        var settings = await service.LoadAsync();
        settings.Theme = "Light";
        settings.ThemeFollowSystem = false;

        await service.SaveAsync(settings);
        var loaded = await service.LoadAsync();

        Assert.Equal("Light", loaded.Theme);
        Assert.False(loaded.ThemeFollowSystem);
    }

    [Fact]
    public async Task SettingsServiceBackfillsInvalidMemoryMode()
    {
        Directory.CreateDirectory(TempRoot);
        await File.WriteAllTextAsync(
            Path.Combine(TempRoot, "settings.json"),
            """
            {
              "DefaultMemorySettingsMode": 999
            }
            """);
        var service = new JsonSettingsService(TempRoot);

        var loaded = await service.LoadAsync();

        Assert.Equal(MemorySettingsMode.Auto, loaded.DefaultMemorySettingsMode);
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
    public async Task SettingsServiceDefaultsDownloadSourcePreferenceToAutoWhenMissing()
    {
        Directory.CreateDirectory(TempRoot);
        await File.WriteAllTextAsync(Path.Combine(TempRoot, "settings.json"), "{}");
        var service = new JsonSettingsService(TempRoot);

        var loaded = await service.LoadAsync();

        Assert.Equal(DownloadSourcePreference.Auto, loaded.DownloadSourcePreference);
    }

    [Fact]
    public async Task SettingsServiceBackfillsInvalidDownloadSourcePreference()
    {
        Directory.CreateDirectory(TempRoot);
        await File.WriteAllTextAsync(
            Path.Combine(TempRoot, "settings.json"),
            """
            {
              "DownloadSourcePreference": 999
            }
            """);
        var service = new JsonSettingsService(TempRoot);

        var loaded = await service.LoadAsync();

        Assert.Equal(DownloadSourcePreference.Auto, loaded.DownloadSourcePreference);
    }

    [Fact]
    public async Task SettingsServiceBackfillsNegativeDownloadSpeedLimit()
    {
        Directory.CreateDirectory(TempRoot);
        await File.WriteAllTextAsync(
            Path.Combine(TempRoot, "settings.json"),
            """
            {
              "DownloadSpeedLimitMbPerSecond": -5
            }
            """);
        var service = new JsonSettingsService(TempRoot);

        var loaded = await service.LoadAsync();

        Assert.Equal(0, loaded.DownloadSpeedLimitMbPerSecond);
    }

    [Theory]
    [InlineData(DownloadSourcePreference.Auto)]
    [InlineData(DownloadSourcePreference.Official)]
    [InlineData(DownloadSourcePreference.BmclApi)]
    public async Task SettingsServiceRoundTripsDownloadSourcePreference(DownloadSourcePreference preference)
    {
        var service = new JsonSettingsService(TempRoot);
        var settings = await service.LoadAsync();
        settings.DownloadSourcePreference = preference;

        await service.SaveAsync(settings);
        var loaded = await service.LoadAsync();

        Assert.Equal(preference, loaded.DownloadSourcePreference);
    }

    [Fact]
    public async Task SettingsServiceRoundTripsDownloadSpeedLimit()
    {
        var service = new JsonSettingsService(TempRoot);
        var settings = await service.LoadAsync();
        settings.DownloadSpeedLimitMbPerSecond = 32;

        await service.SaveAsync(settings);
        var loaded = await service.LoadAsync();

        Assert.Equal(32, loaded.DownloadSpeedLimitMbPerSecond);
    }

    [Fact]
    public async Task SettingsServicePreservesCustomMinecraftDirectory()
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

        Assert.Equal(Path.GetFullPath(staleMinecraftDirectory), loaded.MinecraftDirectory);
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

