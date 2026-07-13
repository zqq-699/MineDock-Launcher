/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Diagnostics;
using Launcher.Application.Services;
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
    public async Task BootstrapLanguageReaderFallsBackWhenSettingsLockRemainsOccupied()
    {
        Directory.CreateDirectory(TempRoot);
        var settingsPath = Path.Combine(TempRoot, "settings.json");
        await File.WriteAllTextAsync(settingsPath, """{"LauncherLanguage":"ja-JP"}""");
        var lockPath = settingsPath + ".lock";
        var readyPath = Path.Combine(TempRoot, "settings-lock-ready");
        var releasePath = Path.Combine(TempRoot, "settings-lock-release");
        var scriptPath = Path.Combine(TempRoot, "hold-settings-lock.ps1");
        await File.WriteAllTextAsync(
            scriptPath,
            $$"""
            $stream = [System.IO.File]::Open(
                '{{lockPath}}',
                [System.IO.FileMode]::OpenOrCreate,
                [System.IO.FileAccess]::ReadWrite,
                [System.IO.FileShare]::None)
            try {
                Set-Content -LiteralPath '{{readyPath}}' -Value 'ready'
                while (-not (Test-Path -LiteralPath '{{releasePath}}')) {
                    Start-Sleep -Milliseconds 25
                }
            }
            finally {
                $stream.Dispose()
            }
            """);
        using var lockHolder = Process.Start(CreatePowerShellStartInfo(scriptPath))
            ?? throw new InvalidOperationException("The settings lock holder process could not be started.");

        try
        {
            await WaitUntilAsync(
                () => File.Exists(readyPath),
                TimeSpan.FromSeconds(5),
                "The independent settings lock holder did not become ready.");
            var readTask = Task.Run(() =>
                new JsonSettingsService(TempRoot).LoadLauncherLanguageForBootstrap());

            var language = await readTask.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(LauncherDefaults.DefaultLauncherLanguage, language);
        }
        finally
        {
            await File.WriteAllTextAsync(releasePath, "release");
            try
            {
                await lockHolder.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                lockHolder.Kill(entireProcessTree: true);
                await lockHolder.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        Assert.Equal(
            "ja-JP",
            new JsonSettingsService(TempRoot).LoadLauncherLanguageForBootstrap());
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
        settings.HasAcceptedUserAgreement = true;

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
        Assert.True(loaded.HasAcceptedUserAgreement);
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
        Assert.False(loaded.HasAcceptedUserAgreement);
    }

    [Fact]
    public async Task StaleLoadedSnapshotsMergeDifferentFieldsInsteadOfOverwritingEachOther()
    {
        var firstProcess = new JsonSettingsService(TempRoot);
        var secondProcess = new JsonSettingsService(TempRoot);
        var firstSnapshot = await firstProcess.LoadAsync();
        var secondSnapshot = await secondProcess.LoadAsync();
        firstSnapshot.DisableBackgroundBlur = true;
        secondSnapshot.DefaultInstanceId = "new-default";

        await firstProcess.SaveAsync(firstSnapshot);
        await secondProcess.SaveAsync(secondSnapshot);

        var loaded = await new JsonSettingsService(TempRoot).LoadAsync();
        Assert.True(loaded.DisableBackgroundBlur);
        Assert.Equal("new-default", loaded.DefaultInstanceId);
    }

    [Fact]
    public async Task ConcurrentAtomicUpdatesPreserveAllIndependentFields()
    {
        var firstProcess = new JsonSettingsService(TempRoot);
        var secondProcess = new JsonSettingsService(TempRoot);
        _ = await firstProcess.LoadAsync();

        await Task.WhenAll(
            firstProcess.UpdateAsync(settings => settings.DisableBackgroundBlur = true),
            secondProcess.UpdateAsync(settings => settings.HasAcceptedUserAgreement = true));

        var loaded = await new JsonSettingsService(TempRoot).LoadAsync();
        Assert.True(loaded.DisableBackgroundBlur);
        Assert.True(loaded.HasAcceptedUserAgreement);
    }

    [Fact]
    public async Task SavingForeignStaleSnapshotFailsInsteadOfReplacingNewerSettings()
    {
        var owner = new JsonSettingsService(TempRoot);
        var foreignSaver = new JsonSettingsService(TempRoot);
        var stale = await owner.LoadAsync();
        await owner.UpdateAsync(settings => settings.DisableBackgroundBlur = true);
        stale.DefaultInstanceId = "stale-default";

        var exception = await Assert.ThrowsAsync<SettingsConcurrencyException>(
            () => foreignSaver.SaveAsync(stale));

        Assert.True(exception.ActualRevision > exception.ExpectedRevision);
        var loaded = await owner.LoadAsync();
        Assert.True(loaded.DisableBackgroundBlur);
        Assert.NotEqual("stale-default", loaded.DefaultInstanceId);
    }

    private static ProcessStartInfo CreatePowerShellStartInfo(string scriptPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        return startInfo;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string timeoutMessage)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed >= timeout)
                throw new TimeoutException(timeoutMessage);

            await Task.Delay(25);
        }
    }
}
