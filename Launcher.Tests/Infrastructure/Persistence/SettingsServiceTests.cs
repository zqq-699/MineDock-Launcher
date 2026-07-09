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

        settings.DefaultMemorySettingsMode = MemorySettingsMode.Manual;
        settings.DefaultMemoryMb = 6144;
        await service.SaveAsync(settings);

        var loaded = await service.LoadAsync();

        Assert.Equal(MemorySettingsMode.Manual, loaded.DefaultMemorySettingsMode);
        Assert.Equal(6144, loaded.DefaultMemoryMb);
        Assert.Equal(TempRoot, loaded.DataDirectory);
        Assert.Equal(DefaultMinecraftDirectory, loaded.MinecraftDirectory);
        Assert.Equal(LauncherDefaults.DefaultTheme, loaded.Theme);
        Assert.Equal(LauncherDefaults.DefaultAccentColor, loaded.AccentColor);
        Assert.Equal(LauncherDefaults.DefaultLauncherLanguage, loaded.LauncherLanguage);
        Assert.True(loaded.AutoSetGameLanguageToLauncherLanguage);
        Assert.True(loaded.ThemeFollowSystem);
        Assert.False(loaded.IsHomeLaunchMenuPinned);
        Assert.False(loaded.DisableBackgroundBlur);
        Assert.Equal(LauncherDefaults.DefaultLauncherBackgroundOpacityPercent, loaded.LauncherBackgroundOpacityPercent);
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

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(120, 100)]
    public async Task SettingsServiceClampsLauncherBackgroundOpacity(int storedValue, int expectedValue)
    {
        Directory.CreateDirectory(TempRoot);
        await File.WriteAllTextAsync(
            Path.Combine(TempRoot, "settings.json"),
            $$"""
            {
              "LauncherBackgroundOpacityPercent": {{storedValue}}
            }
            """);
        var service = new JsonSettingsService(TempRoot);

        var loaded = await service.LoadAsync();

        Assert.Equal(expectedValue, loaded.LauncherBackgroundOpacityPercent);
    }

    [Fact]
    public async Task SettingsServiceRoundTripsThemePreference()
    {
        var service = new JsonSettingsService(TempRoot);
        var settings = await service.LoadAsync();
        settings.Theme = "Light";
        settings.ThemeFollowSystem = false;
        settings.DisableBackgroundBlur = true;
        settings.LauncherBackgroundOpacityPercent = 42;

        await service.SaveAsync(settings);
        var loaded = await service.LoadAsync();

        Assert.Equal("Light", loaded.Theme);
        Assert.False(loaded.ThemeFollowSystem);
        Assert.Equal(LauncherDefaults.DefaultAccentColor, loaded.AccentColor);
        Assert.True(loaded.DisableBackgroundBlur);
        Assert.Equal(42, loaded.LauncherBackgroundOpacityPercent);
    }

    [Fact]
    public async Task SettingsServiceRoundTripsAccentColor()
    {
        var service = new JsonSettingsService(TempRoot);
        var settings = await service.LoadAsync();
        settings.AccentColor = LauncherAccentColors.Purple;

        await service.SaveAsync(settings);
        var loaded = await service.LoadAsync();

        Assert.Equal(LauncherAccentColors.Purple, loaded.AccentColor);
    }

    [Fact]
    public async Task SettingsServiceRoundTripsLauncherLanguage()
    {
        var service = new JsonSettingsService(TempRoot);
        var settings = await service.LoadAsync();
        settings.LauncherLanguage = LauncherLanguages.English;

        await service.SaveAsync(settings);
        var loaded = await service.LoadAsync();

        Assert.Equal(LauncherLanguages.English, loaded.LauncherLanguage);
    }

    [Fact]
    public async Task SettingsServiceRoundTripsGameLanguageAutoSyncPreference()
    {
        var service = new JsonSettingsService(TempRoot);
        var settings = await service.LoadAsync();
        settings.AutoSetGameLanguageToLauncherLanguage = false;

        await service.SaveAsync(settings);
        var loaded = await service.LoadAsync();

        Assert.False(loaded.AutoSetGameLanguageToLauncherLanguage);
    }

    [Fact]
    public async Task SettingsServiceBackfillsInvalidLauncherLanguage()
    {
        Directory.CreateDirectory(TempRoot);
        await File.WriteAllTextAsync(
            Path.Combine(TempRoot, "settings.json"),
            """
            {
              "LauncherLanguage": "en-US"
            }
            """);
        var service = new JsonSettingsService(TempRoot);

        var loaded = await service.LoadAsync();

        Assert.Equal(LauncherDefaults.DefaultLauncherLanguage, loaded.LauncherLanguage);
    }

    [Fact]
    public async Task SettingsServiceRoundTripsHomeLaunchMenuPinPreference()
    {
        var service = new JsonSettingsService(TempRoot);
        var settings = await service.LoadAsync();
        settings.IsHomeLaunchMenuPinned = true;

        await service.SaveAsync(settings);
        var loaded = await service.LoadAsync();

        Assert.True(loaded.IsHomeLaunchMenuPinned);
    }

    [Fact]
    public async Task SettingsServiceBackfillsInvalidAccentColor()
    {
        Directory.CreateDirectory(TempRoot);
        await File.WriteAllTextAsync(
            Path.Combine(TempRoot, "settings.json"),
            """
            {
              "AccentColor": "InvalidAccent"
            }
            """);
        var service = new JsonSettingsService(TempRoot);

        var loaded = await service.LoadAsync();

        Assert.Equal(LauncherDefaults.DefaultAccentColor, loaded.AccentColor);
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
    public async Task SettingsServiceLoadsDefaultInstanceIdFromOldSettings()
    {
        Directory.CreateDirectory(TempRoot);
        await File.WriteAllTextAsync(
            Path.Combine(TempRoot, "settings.json"),
            """
            {
              "DefaultInstanceId": "instance-1"
            }
            """);
        var service = new JsonSettingsService(TempRoot);

        var loaded = await service.LoadAsync();

        Assert.Equal("instance-1", loaded.DefaultInstanceId);
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
    public async Task SettingsServiceDoesNotPersistAccountState()
    {
        var service = new JsonSettingsService(TempRoot);
        var settings = await service.LoadAsync();
        settings.OfflineUsername = "Alex";
        settings.SelectedAccountId = "offline-alex";
        settings.AccountsInitialized = true;
        settings.MicrosoftAccountsImported = true;
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
        var json = await File.ReadAllTextAsync(Path.Combine(TempRoot, "settings.json"));

        Assert.DoesNotContain("OfflineUsername", json);
        Assert.DoesNotContain("SelectedAccountId", json);
        Assert.DoesNotContain("AccountsInitialized", json);
        Assert.DoesNotContain("MicrosoftAccountsImported", json);
        Assert.DoesNotContain("Accounts", json);
        Assert.Equal(LauncherDefaults.DefaultOfflineUsername, loaded.OfflineUsername);
        Assert.Null(loaded.SelectedAccountId);
        Assert.False(loaded.AccountsInitialized);
        Assert.Empty(loaded.Accounts);
    }
}

