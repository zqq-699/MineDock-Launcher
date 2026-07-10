/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Infrastructure.Persistence;

namespace Launcher.Tests.Infrastructure.Persistence;

public sealed class SettingsServiceTests : TestTempDirectory
{
    [Theory]
    [InlineData("zh-Hans", "zh-Hans")]
    [InlineData("ja-JP", "ja-JP")]
    [InlineData("unknown", LauncherDefaults.DefaultLauncherLanguage)]
    public async Task BootstrapLanguageReaderUsesPersistedLanguageWithoutRewritingSettings(
        string persistedLanguage,
        string expectedLanguage)
    {
        Directory.CreateDirectory(TempRoot);
        var settingsPath = Path.Combine(TempRoot, "settings.json");
        var originalJson = $$"""{"LauncherLanguage":"{{persistedLanguage}}","FutureSetting":true}""";
        await File.WriteAllTextAsync(settingsPath, originalJson);

        var language = new JsonSettingsService(TempRoot).LoadLauncherLanguageForBootstrap();

        Assert.Equal(expectedLanguage, language);
        Assert.Equal(originalJson, await File.ReadAllTextAsync(settingsPath));
    }

    [Fact]
    public async Task BootstrapLanguageReaderFallsBackForMalformedSettings()
    {
        Directory.CreateDirectory(TempRoot);
        await File.WriteAllTextAsync(Path.Combine(TempRoot, "settings.json"), "{invalid");

        var language = new JsonSettingsService(TempRoot).LoadLauncherLanguageForBootstrap();

        Assert.Equal(LauncherDefaults.DefaultLauncherLanguage, language);
    }

    [Fact]
    public async Task SettingsRoundTripAsOneContract()
    {
        var service = new JsonSettingsService(TempRoot);
        var settings = await service.LoadAsync();
        settings.Theme = "Light";
        settings.ThemeFollowSystem = false;
        settings.AccentColor = "Purple";
        settings.LauncherLanguage = "ja-JP";
        settings.UpdateChannel = LauncherUpdateChannel.Beta;
        settings.DefaultMemorySettingsMode = MemorySettingsMode.Manual;
        settings.DefaultMemoryMb = 6144;
        settings.DownloadSourcePreference = DownloadSourcePreference.BmclApi;
        settings.DownloadSpeedLimitMbPerSecond = 32;

        await service.SaveAsync(settings);
        var loaded = await service.LoadAsync();

        Assert.Equal("Light", loaded.Theme);
        Assert.False(loaded.ThemeFollowSystem);
        Assert.Equal("Purple", loaded.AccentColor);
        Assert.Equal("ja-JP", loaded.LauncherLanguage);
        Assert.Equal(LauncherUpdateChannel.Beta, loaded.UpdateChannel);
        Assert.Equal(6144, loaded.DefaultMemoryMb);
        Assert.Equal(DownloadSourcePreference.BmclApi, loaded.DownloadSourcePreference);
        Assert.Equal(32, loaded.DownloadSpeedLimitMbPerSecond);
    }

    [Fact]
    public async Task InvalidValuesAreNormalizedTogether()
    {
        Directory.CreateDirectory(TempRoot);
        await File.WriteAllTextAsync(Path.Combine(TempRoot, "settings.json"),
            """{"Theme":"Blue","AccentColor":"Unknown","LauncherLanguage":"xx","UpdateChannel":99,"LauncherBackgroundOpacityPercent":120,"DownloadSpeedLimitMbPerSecond":-1}""");

        var loaded = await new JsonSettingsService(TempRoot).LoadAsync();

        Assert.Equal(LauncherDefaults.DefaultTheme, loaded.Theme);
        Assert.Equal(LauncherDefaults.DefaultAccentColor, loaded.AccentColor);
        Assert.Equal(LauncherDefaults.DefaultLauncherLanguage, loaded.LauncherLanguage);
        Assert.Equal(LauncherDefaults.DefaultUpdateChannel, loaded.UpdateChannel);
        Assert.Equal(100, loaded.LauncherBackgroundOpacityPercent);
        Assert.Equal(0, loaded.DownloadSpeedLimitMbPerSecond);
    }

    [Fact]
    public async Task AccountStateIsNotPersistedInSettings()
    {
        var service = new JsonSettingsService(TempRoot);
        var settings = await service.LoadAsync();
        settings.OfflineUsername = "Alex";
        settings.SelectedAccountId = "offline-alex";
        settings.AccountsInitialized = true;
        settings.Accounts = [new LauncherAccountRecord { Id = "offline-alex", DisplayName = "Alex", IsOffline = true }];

        await service.SaveAsync(settings);
        var json = await File.ReadAllTextAsync(Path.Combine(TempRoot, "settings.json"));

        Assert.DoesNotContain("OfflineUsername", json);
        Assert.DoesNotContain("SelectedAccountId", json);
        Assert.DoesNotContain("Accounts", json);
    }

    [Fact]
    public async Task CanceledSavePreservesPreviouslyWrittenSettings()
    {
        var service = new JsonSettingsService(TempRoot);
        var settings = await service.LoadAsync();
        settings.Theme = "Light";
        await service.SaveAsync(settings);

        settings.Theme = "Dark";
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SaveAsync(settings, cancellation.Token));

        var loaded = await service.LoadAsync();
        Assert.Equal("Light", loaded.Theme);
        Assert.Empty(Directory.EnumerateFiles(TempRoot, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task UnknownSettingsFieldsAreIgnoredWithoutChangingKnownValues()
    {
        Directory.CreateDirectory(TempRoot);
        await File.WriteAllTextAsync(
            Path.Combine(TempRoot, "settings.json"),
            """{"Theme":"Light","FutureSetting":{"enabled":true}}""");

        var loaded = await new JsonSettingsService(TempRoot).LoadAsync();

        Assert.Equal("Light", loaded.Theme);
    }
}
