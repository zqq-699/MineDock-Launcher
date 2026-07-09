using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Settings;
using Launcher.Application;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.Settings;

public sealed class SettingsPageViewModelTests
{
    [Fact]
    public void PrimeFromSettingsLoadsSettingsIntoSections()
    {
        var viewModel = CreateViewModel(out _, out _);
        var settings = new LauncherSettings
        {
            DataDirectory = @"C:\Launcher\Data",
            MinecraftDirectory = @"C:\Launcher\.minecraft",
            DownloadSourcePreference = DownloadSourcePreference.BmclApi,
            DownloadSpeedLimitMbPerSecond = 24,
            DefaultMemorySettingsMode = MemorySettingsMode.Manual,
            DefaultMemoryMb = 8192,
            DefaultCheckFilesBeforeLaunch = false,
            DefaultAutoRepairMissingFiles = false,
            DefaultMinimizeLauncherAfterLaunch = true,
            DefaultLaunchFullScreen = true,
            DefaultPreLaunchCommand = "echo before",
            DefaultWaitForPreLaunchCommand = false,
            DefaultPostExitCommand = "echo after",
            DefaultJvmArguments = "-Dfoo=bar",
            DefaultGameArguments = "--demo",
            Theme = "Light",
            AccentColor = LauncherAccentColors.Emerald,
            DisableBackgroundBlur = true,
            LauncherBackgroundOpacityPercent = 72
        };

        viewModel.PrimeFromSettings(settings);

        Assert.Equal(Strings.Settings_SectionGeneral, viewModel.SectionTitle);
        Assert.True(viewModel.IsGeneralSection);
        Assert.Equal(@"C:\Launcher\Data", viewModel.DataDirectory);
        Assert.Equal(@"C:\Launcher\.minecraft", viewModel.MinecraftDirectory);
        Assert.Equal(DownloadSourcePreference.BmclApi, viewModel.SelectedDownloadSourceOption?.Preference);
        Assert.Equal("24", viewModel.DownloadSpeedLimitMbPerSecondText);
        Assert.Equal(MemorySettingsMode.Manual, viewModel.SelectedMemoryModeOption?.Mode);
        Assert.Equal(8192, viewModel.DefaultMemoryMb);
        Assert.True(viewModel.IsMemorySliderEnabled);
        Assert.Equal(1024, viewModel.MemorySliderMinimumMb);
        Assert.Equal(12288, viewModel.MemorySliderMaximumMb);
        Assert.Equal("16.0 GB", viewModel.SystemTotalMemoryText);
        Assert.Equal("8.0 GB", viewModel.SystemAvailableMemoryText);
        Assert.False(viewModel.IsAutomaticMemorySummaryVisible);
        Assert.False(viewModel.DefaultCheckFilesBeforeLaunch);
        Assert.False(viewModel.DefaultAutoRepairMissingFiles);
        Assert.True(viewModel.DefaultMinimizeLauncherAfterLaunch);
        Assert.True(viewModel.DefaultLaunchFullScreen);
        Assert.Equal("echo before", viewModel.DefaultPreLaunchCommand);
        Assert.False(viewModel.DefaultWaitForPreLaunchCommand);
        Assert.Equal("echo after", viewModel.DefaultPostExitCommand);
        Assert.Equal("-Dfoo=bar", viewModel.DefaultJvmArguments);
        Assert.Equal("--demo", viewModel.DefaultGameArguments);
        Assert.True(viewModel.FollowSystemTheme);
        Assert.False(viewModel.IsThemeSelectionVisible);
        Assert.Equal("Light", viewModel.SelectedThemeOption?.Id);
        Assert.Equal(LauncherAccentColors.Emerald, viewModel.SelectedAccentColorOption?.Id);
        Assert.Equal(Strings.Settings_LanguageSimplifiedChinese, viewModel.Language.SelectedLanguageOption?.Title);
        Assert.Equal(LauncherDefaults.DefaultLauncherLanguage, viewModel.Language.SelectedLanguageId);
        Assert.True(viewModel.Language.AutoSetGameLanguageToLauncherLanguage);
        Assert.True(viewModel.DisableBackgroundBlur);
        Assert.Equal(72, viewModel.LauncherBackgroundOpacityPercent);
        Assert.Equal("72%", viewModel.LauncherBackgroundOpacityText);
        Assert.Equal(LauncherUpdateChannel.Release, viewModel.SelectedUpdateChannelOption?.Channel);
    }

    [Fact]
    public void LanguageOptionsIncludeSimplifiedChineseTraditionalChineseJapaneseAndEnglish()
    {
        var viewModel = CreateViewModel(out _, out _);

        Assert.Equal(
            [
                LauncherLanguages.SimplifiedChinese,
                LauncherLanguages.TraditionalChinese,
                LauncherLanguages.Japanese,
                LauncherLanguages.English
            ],
            viewModel.Language.LanguageOptions.Select(option => option.Id));
        Assert.Equal(Strings.Settings_LanguageSimplifiedChinese, viewModel.Language.LanguageOptions[0].Title);
        Assert.Equal(Strings.Settings_LanguageTraditionalChinese, viewModel.Language.LanguageOptions[1].Title);
        Assert.Equal(Strings.Settings_LanguageJapanese, viewModel.Language.LanguageOptions[2].Title);
        Assert.Equal(Strings.Settings_LanguageEnglish, viewModel.Language.LanguageOptions[3].Title);
    }

    [Fact]
    public void LanguageRestartNoticeIsHiddenWhenSelectionMatchesCurrentLanguage()
    {
        var settings = new LauncherSettings { LauncherLanguage = LauncherLanguages.SimplifiedChinese };
        var viewModel = CreateViewModel(settings, out _, out _);

        viewModel.PrimeFromSettings(settings);

        Assert.False(viewModel.Language.IsLanguageRestartNoticeVisible);
        Assert.Equal(string.Empty, viewModel.Language.LanguageRestartNoticeText);
    }

    [Fact]
    public void LanguageRestartNoticeShowsCurrentThenTargetLanguage()
    {
        var settings = new LauncherSettings { LauncherLanguage = LauncherLanguages.SimplifiedChinese };
        var viewModel = CreateViewModel(settings, out _, out _);
        viewModel.PrimeFromSettings(settings);

        viewModel.Language.SelectedLanguageOption = viewModel.Language.LanguageOptions.Single(option =>
            option.Id == LauncherLanguages.English);

        Assert.True(viewModel.Language.IsLanguageRestartNoticeVisible);
        Assert.Equal(
            $"语言将在重启应用后生效。{Environment.NewLine}The language will take effect after restarting the app.",
            viewModel.Language.LanguageRestartNoticeText);
    }

    [Fact]
    public void GameLanguageAutoSyncSwitchLoadsFromSettings()
    {
        var settings = new LauncherSettings { AutoSetGameLanguageToLauncherLanguage = false };
        var viewModel = CreateViewModel(settings, out _, out _);

        viewModel.PrimeFromSettings(settings);

        Assert.False(viewModel.Language.AutoSetGameLanguageToLauncherLanguage);
    }

    [Fact]
    public async Task GameLanguageAutoSyncSwitchPersistsSettings()
    {
        var settings = new LauncherSettings { AutoSetGameLanguageToLauncherLanguage = true };
        var viewModel = CreateViewModel(settings, out var settingsService, out _);
        viewModel.PrimeFromSettings(settings);

        viewModel.Language.AutoSetGameLanguageToLauncherLanguage = false;

        await TestAsync.WaitForAsync(() =>
            settingsService.SaveCount >= 1
            && settings.AutoSetGameLanguageToLauncherLanguage == false);

        Assert.False(viewModel.Language.AutoSetGameLanguageToLauncherLanguage);
    }

    [Fact]
    public void SettingsSectionsIncludeLanguageAfterGeneral()
    {
        var viewModel = CreateViewModel(out _, out _);

        Assert.Equal(
            [
                SettingsPageSection.General,
                SettingsPageSection.Language,
                SettingsPageSection.LaunchMemory,
                SettingsPageSection.Java,
                SettingsPageSection.Theme,
                SettingsPageSection.Info
            ],
            viewModel.Sections.Select(section => section.Section));
        Assert.Equal(Strings.Settings_SectionLanguage, viewModel.Sections[1].Title);
        Assert.Equal("setting_page/earth", viewModel.Sections[1].IconKey);
    }

    [Fact]
    public void SelectingLanguageSectionShowsLanguagePage()
    {
        var viewModel = CreateViewModel(out _, out _);

        viewModel.SelectSectionCommand.Execute(viewModel.Sections.Single(section => section.Section is SettingsPageSection.Language));

        Assert.True(viewModel.IsLanguageSection);
        Assert.Equal(Strings.Settings_SectionLanguage, viewModel.SectionTitle);
        Assert.IsType<LanguageSettingsViewModel>(viewModel.CurrentSectionViewModel);
    }

    [Fact]
    public void OpenMinecraftDirectoryCommandUsesFolderService()
    {
        var settings = new LauncherSettings
        {
            MinecraftDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var statusService = new FakeStatusService();
        var folderService = new FakeInstanceFolderService();
        var viewModel = new SettingsPageViewModel(
            settingsService,
            statusService,
            new FakeSystemMemoryService(),
            new FakeJavaRuntimeDiscoveryService(),
            new FakeFilePickerService(),
            folderService,
            new FakeFloatingMessageService(), new FakeThemeService());
        viewModel.PrimeFromSettings(settings);

        viewModel.OpenMinecraftDirectoryCommand.Execute(null);

        Assert.Equal(Path.GetFullPath(settings.MinecraftDirectory), folderService.LastOpenedPath);
        Assert.True(Directory.Exists(settings.MinecraftDirectory));
        Assert.Null(statusService.LastMessage);
    }

    [Fact]
    public async Task ChangeMinecraftDirectoryCommandSavesAndRaisesRefreshEvent()
    {
        var settings = new LauncherSettings
        {
            MinecraftDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), ".minecraft")
        };
        var targetDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "custom-minecraft");
        var settingsService = new TestSettingsService(settings);
        var statusService = new FakeStatusService();
        var filePickerService = new FakeFilePickerService { FolderPath = targetDirectory };
        var viewModel = new SettingsPageViewModel(
            settingsService,
            statusService,
            new FakeSystemMemoryService(),
            new FakeJavaRuntimeDiscoveryService(),
            filePickerService,
            new FakeInstanceFolderService(),
            new FakeFloatingMessageService(), new FakeThemeService());
        viewModel.PrimeFromSettings(settings);
        var changedDirectory = string.Empty;
        var changedCount = 0;
        viewModel.MinecraftDirectoryChanged += (_, e) =>
        {
            changedCount++;
            changedDirectory = e.MinecraftDirectory;
        };

        await viewModel.ChangeMinecraftDirectoryCommand.ExecuteAsync(null);

        var normalizedDirectory = Path.GetFullPath(targetDirectory);
        Assert.Equal(normalizedDirectory, viewModel.MinecraftDirectory);
        Assert.Equal(normalizedDirectory, settings.MinecraftDirectory);
        Assert.Equal(normalizedDirectory, changedDirectory);
        Assert.Equal(1, changedCount);
        Assert.Equal(1, settingsService.SaveCount);
        Assert.True(Directory.Exists(normalizedDirectory));
        Assert.Equal(Strings.Status_MinecraftDirectoryChanged, statusService.LastMessage);
    }

    [Fact]
    public async Task InfoCheckUpdatesShowsDialogWhenUpdateIsAvailable()
    {
        var viewModel = CreateViewModel(
            out _,
            out _,
            out _,
            out var updateService);
        updateService.Result = LauncherUpdateCheckResult.Available(
            "1.0.5",
            new LauncherUpdateInfo(
                "1.0.1",
                "1.0.1",
                "https://example.test/releases/v1.0.1",
                "https://example.test/downloads/MineDock_Launcher_x64.exe",
                "Release notes",
                "MineDock_Launcher_x64.exe",
                LauncherUpdateAssetKind.WindowsX64Executable));

        await viewModel.Info.CheckUpdatesCommand.ExecuteAsync(null);

        Assert.Equal("0.9.1-beta.10", updateService.LastCurrentVersion);
        Assert.Equal(LauncherUpdateChannel.Release, updateService.LastChannel);
        Assert.True(viewModel.Info.IsUpdateAvailableDialogOpen);
        Assert.Equal("1.0.1", viewModel.Info.UpdateDialogVersionText);
        Assert.Equal(
            string.Format(Strings.Dialog_UpdateAvailableVersionFormat, "1.0.1"),
            viewModel.Info.UpdateDialogMessage);
    }

    [Fact]
    public async Task InfoStartupUpdateCheckShowsDialogWhenUpdateIsAvailable()
    {
        var viewModel = CreateViewModel(
            new LauncherSettings(),
            new FakeJavaRuntimeDiscoveryService(),
            new FakeFilePickerService(),
            out _,
            out var statusService,
            out var floatingMessageService,
            out _,
            out _,
            out var updateService);
        updateService.Result = LauncherUpdateCheckResult.Available(
            "1.0.5",
            new LauncherUpdateInfo(
                "1.0.1",
                "1.0.1",
                "https://example.test/releases/v1.0.1",
                "https://example.test/downloads/MineDock_Launcher_x64.exe",
                "Release notes",
                "MineDock_Launcher_x64.exe",
                LauncherUpdateAssetKind.WindowsX64Executable));

        await viewModel.Info.CheckUpdatesOnStartupAsync();

        Assert.Equal("0.9.1-beta.10", updateService.LastCurrentVersion);
        Assert.Equal(LauncherUpdateChannel.Release, updateService.LastChannel);
        Assert.True(viewModel.Info.IsUpdateAvailableDialogOpen);
        Assert.Equal("1.0.1", viewModel.Info.UpdateDialogVersionText);
        Assert.Equal(
            string.Format(Strings.Dialog_UpdateAvailableVersionFormat, "1.0.1"),
            viewModel.Info.UpdateDialogMessage);
        Assert.Null(statusService.LastMessage);
        Assert.Null(floatingMessageService.LastMessage);
    }

    [Fact]
    public async Task InfoStartupUpdateCheckIsSilentWhenLauncherIsLatest()
    {
        var viewModel = CreateViewModel(
            new LauncherSettings(),
            new FakeJavaRuntimeDiscoveryService(),
            new FakeFilePickerService(),
            out _,
            out var statusService,
            out var floatingMessageService,
            out _,
            out _,
            out var updateService);
        updateService.Result = LauncherUpdateCheckResult.Latest("1.0.5");

        await viewModel.Info.CheckUpdatesOnStartupAsync();

        Assert.Equal(1, updateService.CallCount);
        Assert.False(viewModel.Info.IsUpdateAvailableDialogOpen);
        Assert.Null(statusService.LastMessage);
        Assert.Null(floatingMessageService.LastMessage);
    }

    [Fact]
    public async Task InfoStartupUpdateCheckIsSilentWhenCheckFails()
    {
        var failedViewModel = CreateViewModel(
            new LauncherSettings(),
            new FakeJavaRuntimeDiscoveryService(),
            new FakeFilePickerService(),
            out _,
            out var failedStatusService,
            out var failedFloatingMessageService,
            out _,
            out _,
            out var failedUpdateService);
        failedUpdateService.Result = LauncherUpdateCheckResult.Failed("1.0.5");

        await failedViewModel.Info.CheckUpdatesOnStartupAsync();

        Assert.False(failedViewModel.Info.IsUpdateAvailableDialogOpen);
        Assert.Null(failedStatusService.LastMessage);
        Assert.Null(failedFloatingMessageService.LastMessage);

        var throwingViewModel = CreateViewModel(
            new LauncherSettings(),
            new FakeJavaRuntimeDiscoveryService(),
            new FakeFilePickerService(),
            out _,
            out var throwingStatusService,
            out var throwingFloatingMessageService,
            out _,
            out _,
            out var throwingUpdateService);
        throwingUpdateService.ExceptionToThrow = new InvalidOperationException("Update check failed.");

        await throwingViewModel.Info.CheckUpdatesOnStartupAsync();

        Assert.False(throwingViewModel.Info.IsUpdateAvailableDialogOpen);
        Assert.Null(throwingStatusService.LastMessage);
        Assert.Null(throwingFloatingMessageService.LastMessage);
    }

    [Fact]
    public async Task InfoCheckUpdatesReportsWhenLauncherIsLatest()
    {
        var viewModel = CreateViewModel(
            new LauncherSettings(),
            new FakeJavaRuntimeDiscoveryService(),
            new FakeFilePickerService(),
            out _,
            out var statusService,
            out var floatingMessageService,
            out _,
            out _,
            out var updateService);
        updateService.Result = LauncherUpdateCheckResult.Latest("1.0.5");

        await viewModel.Info.CheckUpdatesCommand.ExecuteAsync(null);

        Assert.False(viewModel.Info.IsUpdateAvailableDialogOpen);
        Assert.Equal(Strings.Status_LauncherAlreadyLatest, statusService.LastMessage);
        Assert.Equal(Strings.Status_LauncherAlreadyLatest, floatingMessageService.LastMessage);
    }

    [Fact]
    public async Task InfoConfirmUpdateCommandStartsSelfUpdateAndExits()
    {
        var viewModel = CreateViewModel(
            out _,
            out var statusService,
            out _,
            out var updateService,
            out var selfUpdateService,
            out var exitService);
        updateService.Result = LauncherUpdateCheckResult.Available(
            "1.0.5",
            new LauncherUpdateInfo(
                "1.0.1",
                "1.0.1",
                "https://example.test/releases/v1.0.1",
                "https://example.test/downloads/MineDock_Launcher_x64.exe",
                null,
                "MineDock_Launcher_x64.exe",
                LauncherUpdateAssetKind.WindowsX64Executable));

        await viewModel.Info.CheckUpdatesCommand.ExecuteAsync(null);
        await viewModel.Info.ConfirmUpdateCommand.ExecuteAsync(null);

        Assert.Equal("1.0.1", selfUpdateService.LastUpdate?.Version);
        Assert.False(viewModel.Info.IsUpdateAvailableDialogOpen);
        Assert.Equal(Strings.Status_LauncherUpdateRestarting, statusService.LastMessage);
        Assert.Equal(1, exitService.ShutdownCount);
    }

    [Fact]
    public async Task InfoConfirmUpdateCommandReportsWhenSelfUpdateFails()
    {
        var viewModel = CreateViewModel(
            out _,
            out var statusService,
            out _,
            out var updateService,
            out var selfUpdateService,
            out var exitService);
        selfUpdateService.Result = LauncherSelfUpdateStartResult.Failed();
        updateService.Result = LauncherUpdateCheckResult.Available(
            "1.0.5",
            new LauncherUpdateInfo(
                "1.0.1",
                "1.0.1",
                "https://example.test/releases/v1.0.1",
                "https://example.test/downloads/MineDock_Launcher_x64.exe",
                null,
                "MineDock_Launcher_x64.exe",
                LauncherUpdateAssetKind.WindowsX64Executable));

        await viewModel.Info.CheckUpdatesCommand.ExecuteAsync(null);
        await viewModel.Info.ConfirmUpdateCommand.ExecuteAsync(null);

        Assert.Equal("1.0.1", selfUpdateService.LastUpdate?.Version);
        Assert.True(viewModel.Info.IsUpdateAvailableDialogOpen);
        Assert.Equal(Strings.Status_LauncherUpdateStartFailed, statusService.LastMessage);
        Assert.Equal(0, exitService.ShutdownCount);
    }

    [Fact]
    public async Task InfoCheckUpdatesReportsFailureWhenServiceFails()
    {
        var viewModel = CreateViewModel(
            out _,
            out var statusService,
            out _,
            out var updateService);
        updateService.Result = LauncherUpdateCheckResult.Failed("1.0.5");

        await viewModel.Info.CheckUpdatesCommand.ExecuteAsync(null);

        Assert.False(viewModel.Info.IsUpdateAvailableDialogOpen);
        Assert.Equal(Strings.Status_CheckUpdatesFailed, statusService.LastMessage);
    }

    [Fact]
    public async Task UpdateChannelSelectionPersistsSettings()
    {
        var settings = new LauncherSettings();
        var viewModel = CreateViewModel(settings, out var settingsService, out _);
        viewModel.PrimeFromSettings(settings);

        viewModel.SelectedUpdateChannelOption = viewModel.UpdateChannelOptions.Single(option =>
            option.Channel is LauncherUpdateChannel.Beta);

        await TestAsync.WaitForAsync(() =>
            settingsService.SaveCount >= 1
            && settings.UpdateChannel is LauncherUpdateChannel.Beta);

        Assert.Equal(LauncherUpdateChannel.Beta, viewModel.SelectedUpdateChannelOption?.Channel);
    }

    [Fact]
    public async Task InfoCheckUpdatesUsesSelectedUpdateChannel()
    {
        var settings = new LauncherSettings { UpdateChannel = LauncherUpdateChannel.Beta };
        var viewModel = CreateViewModel(
            settings,
            new FakeJavaRuntimeDiscoveryService(),
            new FakeFilePickerService(),
            out _,
            out _,
            out _,
            out _,
            out _,
            out var updateService);
        viewModel.PrimeFromSettings(settings);

        await viewModel.Info.CheckUpdatesCommand.ExecuteAsync(null);

        Assert.Equal(LauncherUpdateChannel.Beta, updateService.LastChannel);
    }

    [Fact]
    public async Task ThemeControlsApplyAndPersistSettings()
    {
        var settings = new LauncherSettings { Theme = LauncherDefaults.DefaultTheme };
        var viewModel = CreateViewModel(settings, out var settingsService, out _, out var themeService);
        viewModel.PrimeFromSettings(settings);

        viewModel.FollowSystemTheme = false;
        viewModel.SelectedThemeOption = viewModel.ThemeOptions.Single(option => option.Id == "Light");

        await TestAsync.WaitForAsync(() =>
            settingsService.SaveCount >= 1
            && settings.Theme == "Light"
            && settings.ThemeFollowSystem == false);

        Assert.Equal("Light", themeService.LastTheme);
        Assert.False(themeService.LastFollowSystem);
        Assert.Equal(LauncherDefaults.DefaultLauncherBackgroundOpacityPercent, themeService.LastBackgroundOpacityPercent);
    }

    [Fact]
    public async Task AccentColorSelectionAppliesAndPersistsSettings()
    {
        var settings = new LauncherSettings { AccentColor = LauncherDefaults.DefaultAccentColor };
        var viewModel = CreateViewModel(settings, out var settingsService, out _, out var themeService);
        viewModel.PrimeFromSettings(settings);

        viewModel.SelectedAccentColorOption = viewModel.AccentColorOptions.Single(option => option.Id == LauncherAccentColors.Pink);

        await TestAsync.WaitForAsync(() =>
            settingsService.SaveCount >= 1
            && settings.AccentColor == LauncherAccentColors.Pink);

        Assert.Equal(LauncherAccentColors.Pink, viewModel.SelectedAccentColorOption?.Id);
        Assert.Equal(LauncherAccentColors.Pink, themeService.LastAccentColor);
        Assert.Equal(1, themeService.ApplyAccentCount);
    }

    [Fact]
    public async Task LanguageSelectionPersistsSettings()
    {
        var settings = new LauncherSettings { LauncherLanguage = LauncherDefaults.DefaultLauncherLanguage };
        var viewModel = CreateViewModel(settings, out var settingsService, out _);
        viewModel.PrimeFromSettings(settings);

        viewModel.Language.SelectedLanguageOption = viewModel.Language.LanguageOptions.Single(option =>
            option.Id == LauncherLanguages.English);

        await TestAsync.WaitForAsync(() =>
            settingsService.SaveCount >= 1
            && settings.LauncherLanguage == LauncherLanguages.English);

        Assert.Equal(Strings.Settings_LanguageEnglish, viewModel.Language.SelectedLanguageOption?.Title);
        Assert.Equal(LauncherLanguages.English, viewModel.Language.SelectedLanguageId);
    }

    [Fact]
    public void LanguageSelectionLoadsEnglishSettings()
    {
        var settings = new LauncherSettings { LauncherLanguage = LauncherLanguages.English };
        var viewModel = CreateViewModel(settings, out _, out _);

        viewModel.PrimeFromSettings(settings);

        Assert.Equal(Strings.Settings_LanguageEnglish, viewModel.Language.SelectedLanguageOption?.Title);
        Assert.Equal(LauncherLanguages.English, viewModel.Language.SelectedLanguageId);
    }

    [Fact]
    public void LanguageSelectionLoadsTraditionalChineseSettings()
    {
        var settings = new LauncherSettings { LauncherLanguage = LauncherLanguages.TraditionalChinese };
        var viewModel = CreateViewModel(settings, out _, out _);

        viewModel.PrimeFromSettings(settings);

        Assert.Equal(Strings.Settings_LanguageTraditionalChinese, viewModel.Language.SelectedLanguageOption?.Title);
        Assert.Equal(LauncherLanguages.TraditionalChinese, viewModel.Language.SelectedLanguageId);
    }

    [Fact]
    public void LanguageSelectionLoadsJapaneseSettings()
    {
        var settings = new LauncherSettings { LauncherLanguage = LauncherLanguages.Japanese };
        var viewModel = CreateViewModel(settings, out _, out _);

        viewModel.PrimeFromSettings(settings);

        Assert.Equal(Strings.Settings_LanguageJapanese, viewModel.Language.SelectedLanguageOption?.Title);
        Assert.Equal(LauncherLanguages.Japanese, viewModel.Language.SelectedLanguageId);
    }

    [Fact]
    public void LanguageSelectionFallsBackForInvalidSettings()
    {
        var settings = new LauncherSettings { LauncherLanguage = "legacy-language" };
        var viewModel = CreateViewModel(settings, out _, out _);

        viewModel.PrimeFromSettings(settings);

        Assert.Equal(Strings.Settings_LanguageSimplifiedChinese, viewModel.Language.SelectedLanguageOption?.Title);
        Assert.Equal(LauncherDefaults.DefaultLauncherLanguage, viewModel.Language.SelectedLanguageId);
    }

    [Fact]
    public async Task BackgroundOpacitySliderAppliesAndPersistsSettings()
    {
        var settings = new LauncherSettings
        {
            LauncherBackgroundOpacityPercent = LauncherDefaults.DefaultLauncherBackgroundOpacityPercent
        };
        var viewModel = CreateViewModel(settings, out var settingsService, out _, out var themeService);
        viewModel.PrimeFromSettings(settings);

        viewModel.LauncherBackgroundOpacityPercent = 42;

        await TestAsync.WaitForAsync(() =>
            settingsService.SaveCount >= 1
            && settings.LauncherBackgroundOpacityPercent == 42);

        Assert.Equal(42, viewModel.LauncherBackgroundOpacityPercent);
        Assert.Equal("42%", viewModel.LauncherBackgroundOpacityText);
        Assert.Equal(1, themeService.ApplyBackgroundOpacityCount);
        Assert.Equal(42, themeService.LastBackgroundOpacityPercent);
    }

    [Fact]
    public async Task DisableBackgroundBlurToggleAppliesAndPersistsSettings()
    {
        var settings = new LauncherSettings();
        var viewModel = CreateViewModel(settings, out var settingsService, out _, out var themeService);
        viewModel.PrimeFromSettings(settings);

        viewModel.DisableBackgroundBlur = true;

        await TestAsync.WaitForAsync(() =>
            settingsService.SaveCount >= 1
            && settings.DisableBackgroundBlur);

        Assert.True(viewModel.DisableBackgroundBlur);
        Assert.Equal(1, themeService.ApplyBackgroundBlurDisabledCount);
        Assert.True(themeService.LastDisableBackgroundBlur);
    }

    [Fact]
    public async Task ManualJavaSelectionSavesSelectedRuntimePath()
    {
        var selectedRuntime = CreateJavaRuntime(@"C:\Java\jdk-17\bin\java.exe", 17);
        var settings = new LauncherSettings
        {
            JavaSelectionMode = JavaSelectionMode.Manual
        };
        var javaRuntimeDiscoveryService = new FakeJavaRuntimeDiscoveryService
        {
            Runtimes =
            [
                CreateJavaRuntime(@"C:\Java\jdk-21\bin\java.exe", 21),
                selectedRuntime
            ]
        };
        var viewModel = CreateViewModel(settings, javaRuntimeDiscoveryService, out var settingsService, out _);
        viewModel.PrimeFromSettings(settings);

        await viewModel.RefreshJavaRuntimesCommand.ExecuteAsync(null);
        viewModel.SelectedJavaRuntime = viewModel.JavaRuntimes.Single(item => item.ExecutablePath == selectedRuntime.ExecutablePath);

        await TestAsync.WaitForAsync(() =>
            settingsService.SaveCount >= 1
            && settings.JavaSelectionMode is JavaSelectionMode.Manual
            && settings.SelectedJavaExecutablePath == selectedRuntime.ExecutablePath);
    }

    [Fact]
    public async Task RefreshJavaRuntimesReportsFriendlyMessageWhenDiscoveryFails()
    {
        var javaRuntimeDiscoveryService = new FakeJavaRuntimeDiscoveryService
        {
            ExceptionToThrow = new InvalidOperationException("boom")
        };
        var viewModel = CreateViewModel(
            new LauncherSettings(),
            javaRuntimeDiscoveryService,
            out _,
            out var statusService);

        await viewModel.RefreshJavaRuntimesCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.JavaRuntimes);
        Assert.Equal(Strings.Settings_JavaListEmpty, viewModel.JavaRuntimeListMessage);
        Assert.Equal(Strings.Status_JavaScanFailed, statusService.LastMessage);
    }

    [Fact]
    public async Task ImportJavaRuntimeAddsPickedRuntime()
    {
        const string executablePath = @"C:\Java\jdk-21\bin\java.exe";
        var javaRuntimeDiscoveryService = new FakeJavaRuntimeDiscoveryService
        {
            ImportedRuntime = new JavaRuntimeInfo(
                "Java 21",
                "21.0.2",
                21,
                "x64",
                executablePath,
                @"C:\Java\jdk-21",
                "ManualImport")
        };
        var filePickerService = new FakeFilePickerService
        {
            JavaExecutablePath = executablePath
        };
        var viewModel = CreateViewModel(
            new LauncherSettings(),
            javaRuntimeDiscoveryService,
            filePickerService,
            out _,
            out var statusService,
            out _);

        await viewModel.ImportJavaRuntimeCommand.ExecuteAsync(null);

        Assert.Single(viewModel.JavaRuntimes);
        Assert.Equal(executablePath, viewModel.JavaRuntimes[0].ExecutablePath);
        Assert.Equal(executablePath, javaRuntimeDiscoveryService.LastImportedExecutablePath);
        Assert.Equal(Strings.Status_JavaImported, statusService.LastMessage);
        Assert.False(viewModel.HasJavaRuntimeListMessage);
    }

    [Fact]
    public async Task ImportJavaRuntimeReportsFriendlyMessageWhenImportFails()
    {
        var javaRuntimeDiscoveryService = new FakeJavaRuntimeDiscoveryService
        {
            ImportExceptionToThrow = new InvalidOperationException("boom")
        };
        var viewModel = CreateViewModel(
            new LauncherSettings(),
            javaRuntimeDiscoveryService,
            new FakeFilePickerService { JavaExecutablePath = @"C:\bad\java.exe" },
            out _,
            out var statusService);

        await viewModel.ImportJavaRuntimeCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.JavaRuntimes);
        Assert.Equal(Strings.Status_JavaImportFailed, statusService.LastMessage);
    }

    [Fact]
    public async Task EditedSettingsAutoSaveAfterChanges()
    {
        var settings = new LauncherSettings
        {
            DefaultMemorySettingsMode = MemorySettingsMode.Auto,
            DefaultMemoryMb = 4096,
            DefaultCheckFilesBeforeLaunch = true,
            DefaultAutoRepairMissingFiles = true
        };
        var viewModel = CreateViewModel(settings, out var settingsService, out var statusService);
        viewModel.PrimeFromSettings(settings);

        viewModel.SelectedMemoryModeOption = viewModel.MemoryModeOptions.Single(option => option.Mode == MemorySettingsMode.Manual);
        viewModel.DefaultMemoryMb = 12288;
        viewModel.DefaultCheckFilesBeforeLaunch = false;
        viewModel.DefaultAutoRepairMissingFiles = false;
        viewModel.DefaultMinimizeLauncherAfterLaunch = true;
        viewModel.DefaultLaunchFullScreen = true;
        viewModel.DefaultPreLaunchCommand = "echo before";
        viewModel.DefaultWaitForPreLaunchCommand = false;
        viewModel.DefaultPostExitCommand = "echo after";
        viewModel.DefaultJvmArguments = "-Dfoo=bar";
        viewModel.DefaultGameArguments = "--demo";

        await TestAsync.WaitForAsync(() =>
            settingsService.SaveCount >= 1
            && settings.DefaultMemorySettingsMode == MemorySettingsMode.Manual
            && settings.DefaultMemoryMb == 12288
            && settings.DefaultCheckFilesBeforeLaunch == false
            && settings.DefaultAutoRepairMissingFiles == false
            && settings.DefaultMinimizeLauncherAfterLaunch
            && settings.DefaultLaunchFullScreen
            && settings.DefaultPreLaunchCommand == "echo before"
            && !settings.DefaultWaitForPreLaunchCommand
            && settings.DefaultPostExitCommand == "echo after"
            && settings.DefaultJvmArguments == "-Dfoo=bar"
            && settings.DefaultGameArguments == "--demo");

        Assert.True(settingsService.SaveCount >= 1);
        Assert.Equal(MemorySettingsMode.Manual, settings.DefaultMemorySettingsMode);
        Assert.Equal(12288, settings.DefaultMemoryMb);
        Assert.False(settings.DefaultCheckFilesBeforeLaunch);
        Assert.False(settings.DefaultAutoRepairMissingFiles);
        Assert.True(settings.DefaultMinimizeLauncherAfterLaunch);
        Assert.True(settings.DefaultLaunchFullScreen);
        Assert.Equal("echo before", settings.DefaultPreLaunchCommand);
        Assert.False(settings.DefaultWaitForPreLaunchCommand);
        Assert.Equal("echo after", settings.DefaultPostExitCommand);
        Assert.Equal("-Dfoo=bar", settings.DefaultJvmArguments);
        Assert.Equal("--demo", settings.DefaultGameArguments);
        Assert.Null(statusService.LastMessage);
    }

    [Fact]
    public async Task EditingDownloadSpeedLimitAutoSavesNormalizedValue()
    {
        var settings = new LauncherSettings
        {
            DownloadSpeedLimitMbPerSecond = 0
        };
        var viewModel = CreateViewModel(settings, out var settingsService, out _);
        viewModel.PrimeFromSettings(settings);

        viewModel.DownloadSpeedLimitMbPerSecondText = "48";

        await TestAsync.WaitForAsync(() =>
            settingsService.SaveCount >= 1
            && settings.DownloadSpeedLimitMbPerSecond == 48
            && viewModel.DownloadSpeedLimitMbPerSecondText == "48");
    }

    [Fact]
    public async Task ChangingDownloadSourceAutoSavesPreference()
    {
        var settings = new LauncherSettings
        {
            DownloadSourcePreference = DownloadSourcePreference.Auto
        };
        var viewModel = CreateViewModel(settings, out var settingsService, out _);
        viewModel.PrimeFromSettings(settings);

        viewModel.SelectedDownloadSourceOption = viewModel.DownloadSourceOptions.Single(option =>
            option.Preference is DownloadSourcePreference.Official);

        await TestAsync.WaitForAsync(() =>
            settingsService.SaveCount >= 1
            && settings.DownloadSourcePreference is DownloadSourcePreference.Official);
    }

    private static SettingsPageViewModel CreateViewModel(
        out TestSettingsService settingsService,
        out FakeStatusService statusService)
    {
        return CreateViewModel(new LauncherSettings(), out settingsService, out statusService);
    }

    private static SettingsPageViewModel CreateViewModel(
        out TestSettingsService settingsService,
        out FakeStatusService statusService,
        out FakeExternalLinkService externalLinkService)
    {
        return CreateViewModel(
            new LauncherSettings(),
            new FakeJavaRuntimeDiscoveryService(),
            new FakeFilePickerService(),
            out settingsService,
            out statusService,
            out _,
            out _,
            out externalLinkService);
    }

    private static SettingsPageViewModel CreateViewModel(
        out TestSettingsService settingsService,
        out FakeStatusService statusService,
        out FakeExternalLinkService externalLinkService,
        out FakeLauncherUpdateService launcherUpdateService)
    {
        return CreateViewModel(
            out settingsService,
            out statusService,
            out externalLinkService,
            out launcherUpdateService,
            out _,
            out _);
    }

    private static SettingsPageViewModel CreateViewModel(
        out TestSettingsService settingsService,
        out FakeStatusService statusService,
        out FakeExternalLinkService externalLinkService,
        out FakeLauncherUpdateService launcherUpdateService,
        out FakeLauncherSelfUpdateService launcherSelfUpdateService,
        out FakeApplicationExitService applicationExitService)
    {
        return CreateViewModel(
            new LauncherSettings(),
            new FakeJavaRuntimeDiscoveryService(),
            new FakeFilePickerService(),
            out settingsService,
            out statusService,
            out _,
            out _,
            out externalLinkService,
            out launcherUpdateService,
            out launcherSelfUpdateService,
            out applicationExitService);
    }

    private static SettingsPageViewModel CreateViewModel(
        LauncherSettings settings,
        out TestSettingsService settingsService,
        out FakeStatusService statusService)
    {
        return CreateViewModel(settings, out settingsService, out statusService, out _);
    }

    private static SettingsPageViewModel CreateViewModel(
        LauncherSettings settings,
        out TestSettingsService settingsService,
        out FakeStatusService statusService,
        out FakeThemeService themeService)
    {
        return CreateViewModel(settings, new FakeJavaRuntimeDiscoveryService(), new FakeFilePickerService(), out settingsService, out statusService, out _, out themeService);
    }

    [Fact]
    public async Task ManualMemoryValueSavesWithTenthsOfGbPrecision()
    {
        var settings = new LauncherSettings
        {
            DefaultMemorySettingsMode = MemorySettingsMode.Manual,
            DefaultMemoryMb = 4096
        };
        var viewModel = CreateViewModel(settings, out var settingsService, out _);
        viewModel.PrimeFromSettings(settings);

        viewModel.DefaultMemoryMb = 4250;

        await TestAsync.WaitForAsync(() =>
            settingsService.SaveCount >= 1
            && settings.DefaultMemoryMb == 4301);

        Assert.Equal("4.2 GB", viewModel.DefaultMemoryText);
    }

    private static SettingsPageViewModel CreateViewModel(
        LauncherSettings settings,
        FakeJavaRuntimeDiscoveryService javaRuntimeDiscoveryService,
        out TestSettingsService settingsService,
        out FakeStatusService statusService)
    {
        return CreateViewModel(settings, javaRuntimeDiscoveryService, new FakeFilePickerService(), out settingsService, out statusService);
    }

    private static SettingsPageViewModel CreateViewModel(
        LauncherSettings settings,
        FakeJavaRuntimeDiscoveryService javaRuntimeDiscoveryService,
        FakeFilePickerService filePickerService,
        out TestSettingsService settingsService,
        out FakeStatusService statusService)
    {
        return CreateViewModel(
            settings,
            javaRuntimeDiscoveryService,
            filePickerService,
            out settingsService,
            out statusService,
            out _,
            out _);
    }

    private static SettingsPageViewModel CreateViewModel(
        LauncherSettings settings,
        FakeJavaRuntimeDiscoveryService javaRuntimeDiscoveryService,
        FakeFilePickerService filePickerService,
        out TestSettingsService settingsService,
        out FakeStatusService statusService,
        out FakeFloatingMessageService floatingMessageService)
    {
        return CreateViewModel(
            settings,
            javaRuntimeDiscoveryService,
            filePickerService,
            out settingsService,
            out statusService,
            out floatingMessageService,
            out _);
    }

    private static SettingsPageViewModel CreateViewModel(
        LauncherSettings settings,
        FakeJavaRuntimeDiscoveryService javaRuntimeDiscoveryService,
        FakeFilePickerService filePickerService,
        out TestSettingsService settingsService,
        out FakeStatusService statusService,
        out FakeFloatingMessageService floatingMessageService,
        out FakeThemeService themeService)
    {
        return CreateViewModel(
            settings,
            javaRuntimeDiscoveryService,
            filePickerService,
            out settingsService,
            out statusService,
            out floatingMessageService,
            out themeService,
            out _);
    }

    private static SettingsPageViewModel CreateViewModel(
        LauncherSettings settings,
        FakeJavaRuntimeDiscoveryService javaRuntimeDiscoveryService,
        FakeFilePickerService filePickerService,
        out TestSettingsService settingsService,
        out FakeStatusService statusService,
        out FakeFloatingMessageService floatingMessageService,
        out FakeThemeService themeService,
        out FakeExternalLinkService externalLinkService)
    {
        return CreateViewModel(
            settings,
            javaRuntimeDiscoveryService,
            filePickerService,
            out settingsService,
            out statusService,
            out floatingMessageService,
            out themeService,
            out externalLinkService,
            out _);
    }

    private static SettingsPageViewModel CreateViewModel(
        LauncherSettings settings,
        FakeJavaRuntimeDiscoveryService javaRuntimeDiscoveryService,
        FakeFilePickerService filePickerService,
        out TestSettingsService settingsService,
        out FakeStatusService statusService,
        out FakeFloatingMessageService floatingMessageService,
        out FakeThemeService themeService,
        out FakeExternalLinkService externalLinkService,
        out FakeLauncherUpdateService launcherUpdateService)
    {
        return CreateViewModel(
            settings,
            javaRuntimeDiscoveryService,
            filePickerService,
            out settingsService,
            out statusService,
            out floatingMessageService,
            out themeService,
            out externalLinkService,
            out launcherUpdateService,
            out _,
            out _);
    }

    private static SettingsPageViewModel CreateViewModel(
        LauncherSettings settings,
        FakeJavaRuntimeDiscoveryService javaRuntimeDiscoveryService,
        FakeFilePickerService filePickerService,
        out TestSettingsService settingsService,
        out FakeStatusService statusService,
        out FakeFloatingMessageService floatingMessageService,
        out FakeThemeService themeService,
        out FakeExternalLinkService externalLinkService,
        out FakeLauncherUpdateService launcherUpdateService,
        out FakeLauncherSelfUpdateService launcherSelfUpdateService,
        out FakeApplicationExitService applicationExitService)
    {
        settingsService = new TestSettingsService(settings);
        statusService = new FakeStatusService();
        floatingMessageService = new FakeFloatingMessageService();
        themeService = new FakeThemeService();
        externalLinkService = new FakeExternalLinkService();
        launcherUpdateService = new FakeLauncherUpdateService();
        launcherSelfUpdateService = new FakeLauncherSelfUpdateService();
        applicationExitService = new FakeApplicationExitService();
        return new SettingsPageViewModel(
            settingsService,
            statusService,
            new FakeSystemMemoryService(),
            javaRuntimeDiscoveryService,
            filePickerService,
            new FakeInstanceFolderService(),
            floatingMessageService,
            themeService,
            externalLinkService,
            launcherUpdateService,
            launcherSelfUpdateService,
            applicationExitService);
    }

    private static JavaRuntimeInfo CreateJavaRuntime(string executablePath, int majorVersion)
    {
        var installationDirectory = Path.GetDirectoryName(Path.GetDirectoryName(executablePath)) ?? string.Empty;
        return new JavaRuntimeInfo(
            $"Java {majorVersion}",
            $"{majorVersion}.0.0",
            majorVersion,
            "x64",
            executablePath,
            installationDirectory,
            "Test");
    }

    private sealed class FakeStatusService : IStatusService
    {
        public event Action<string>? MessageReported;

        public string? LastMessage { get; private set; }

        public void Report(string message)
        {
            LastMessage = message;
            MessageReported?.Invoke(message);
        }
    }

    private sealed class FakeJavaRuntimeDiscoveryService : IJavaRuntimeDiscoveryService
    {
        public IReadOnlyList<JavaRuntimeInfo> Runtimes { get; init; } = [];

        public JavaRuntimeInfo ImportedRuntime { get; init; } = new(
            "Java",
            null,
            null,
            "unknown",
            @"C:\Java\bin\java.exe",
            @"C:\Java",
            "ManualImport");

        public Exception? ExceptionToThrow { get; init; }

        public Exception? ImportExceptionToThrow { get; init; }

        public string? LastMinecraftDirectory { get; private set; }

        public string? LastImportedExecutablePath { get; private set; }

        public Task<IReadOnlyList<JavaRuntimeInfo>> DiscoverAsync(
            string? minecraftDirectory,
            CancellationToken cancellationToken = default)
        {
            LastMinecraftDirectory = minecraftDirectory;

            if (ExceptionToThrow is not null)
                throw ExceptionToThrow;

            return Task.FromResult(Runtimes);
        }

        public Task<JavaRuntimeInfo> DiscoverExecutableAsync(
            string executablePath,
            CancellationToken cancellationToken = default)
        {
            LastImportedExecutablePath = executablePath;

            if (ImportExceptionToThrow is not null)
                throw ImportExceptionToThrow;

            return Task.FromResult(ImportedRuntime);
        }
    }

    private sealed class FakeSystemMemoryService : ISystemMemoryService
    {
        public SystemMemorySnapshot GetSnapshot()
        {
            return new SystemMemorySnapshot(
                TotalMemoryBytes: 16L * 1024L * 1024L * 1024L,
                AvailableMemoryBytes: 8L * 1024L * 1024L * 1024L);
        }
    }

    private sealed class FakeFilePickerService : IFilePickerService
    {
        public string? JavaExecutablePath { get; init; }
        public string? FolderPath { get; init; }

        public string? PickMinecraftSkin()
        {
            return null;
        }

        public string? PickJavaExecutable()
        {
            return JavaExecutablePath;
        }

        public string? PickModFile()
        {
            return null;
        }

        public string? PickSaveArchive()
        {
            return null;
        }

        public string? PickResourcePackArchive()
        {
            return null;
        }

        public string? PickShaderPackArchive()
        {
            return null;
        }

        public string? PickModpackExportArchive(string defaultFileName, ModpackExportKind kind)
        {
            return null;
        }

        public string? PickLocalImportFile()
        {
            return null;
        }

        public string? PickFolder(string title, string? initialDirectory = null)
        {
            return FolderPath;
        }
    }

    private sealed class FakeInstanceFolderService : IInstanceFolderService
    {
        public string? LastOpenedPath { get; private set; }
        public string? LastRevealedFilePath { get; private set; }

        public bool DirectoryExists(string folderPath)
        {
            return !string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath);
        }

        public string EnsureDirectoryExists(string folderPath)
        {
            var normalizedFolderPath = Path.GetFullPath(folderPath);
            Directory.CreateDirectory(normalizedFolderPath);
            return normalizedFolderPath;
        }

        public bool TryOpen(string folderPath)
        {
            LastOpenedPath = folderPath;
            return true;
        }

        public bool TryRevealFile(string filePath)
        {
            LastRevealedFilePath = filePath;
            return true;
        }
    }

    private sealed class FakeExternalLinkService : IExternalLinkService
    {
        public string? LastOpenedUrl { get; private set; }
        public bool TryOpenResult { get; set; } = true;

        public bool TryOpen(string url)
        {
            LastOpenedUrl = url;
            return TryOpenResult;
        }
    }

    private sealed class FakeLauncherUpdateService : ILauncherUpdateService
    {
        public LauncherUpdateCheckResult Result { get; set; } = LauncherUpdateCheckResult.Latest("1.0.5");
        public Task<LauncherUpdateCheckResult>? ResultTask { get; set; }
        public Exception? ExceptionToThrow { get; set; }
        public string? LastCurrentVersion { get; private set; }
        public LauncherUpdateChannel? LastChannel { get; private set; }
        public int CallCount { get; private set; }

        public Task<LauncherUpdateCheckResult> CheckForUpdatesAsync(
            string currentVersion,
            LauncherUpdateChannel channel,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastCurrentVersion = currentVersion;
            LastChannel = channel;
            if (ExceptionToThrow is not null)
                throw ExceptionToThrow;

            return ResultTask ?? Task.FromResult(Result);
        }
    }

    private sealed class FakeLauncherSelfUpdateService : ILauncherSelfUpdateService
    {
        public LauncherSelfUpdateStartResult Result { get; set; } = LauncherSelfUpdateStartResult.Success("update.exe");
        public LauncherUpdateInfo? LastUpdate { get; private set; }

        public Task<LauncherSelfUpdateStartResult> StartUpdateAsync(
            LauncherUpdateInfo update,
            CancellationToken cancellationToken = default)
        {
            LastUpdate = update;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeApplicationExitService : IApplicationExitService
    {
        public int ShutdownCount { get; private set; }

        public void Shutdown()
        {
            ShutdownCount++;
        }
    }

    private sealed class FakeFloatingMessageService : IFloatingMessageService
    {
        public event Action<string>? MessageRequested;

        public string? LastMessage { get; private set; }

        public void Show(string message)
        {
            LastMessage = message;
            MessageRequested?.Invoke(message);
        }
    }

}
