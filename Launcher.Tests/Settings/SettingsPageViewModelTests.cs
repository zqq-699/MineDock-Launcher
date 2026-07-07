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
        Assert.True(viewModel.DisableBackgroundBlur);
        Assert.Equal(72, viewModel.LauncherBackgroundOpacityPercent);
        Assert.Equal("72%", viewModel.LauncherBackgroundOpacityText);
    }

    [Fact]
    public void SettingsSectionsHideControlList()
    {
        var viewModel = CreateViewModel(out _, out _);

        Assert.Equal(
            [
                SettingsPageSection.General,
                SettingsPageSection.LaunchMemory,
                SettingsPageSection.Java,
                SettingsPageSection.Theme,
                SettingsPageSection.Info
            ],
            viewModel.Sections.Select(section => section.Section));
        Assert.DoesNotContain(viewModel.Sections, section => section.Section is SettingsPageSection.ControlList);
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
    public void OpenLauncherLogDirectoryCommandUsesFolderService()
    {
        var settings = new LauncherSettings();
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

        viewModel.OpenLauncherLogDirectoryCommand.Execute(null);

        Assert.Equal(viewModel.LauncherLogDirectory, folderService.LastOpenedPath);
        Assert.EndsWith("log", viewModel.LauncherLogDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(viewModel.LauncherLogDirectory));
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
    public async Task ChangeMinecraftDirectoryCommandDoesNothingWhenPickerIsCanceled()
    {
        var settings = new LauncherSettings
        {
            MinecraftDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), ".minecraft")
        };
        var settingsService = new TestSettingsService(settings);
        var viewModel = new SettingsPageViewModel(
            settingsService,
            new FakeStatusService(),
            new FakeSystemMemoryService(),
            new FakeJavaRuntimeDiscoveryService(),
            new FakeFilePickerService(),
            new FakeInstanceFolderService(),
            new FakeFloatingMessageService(), new FakeThemeService());
        viewModel.PrimeFromSettings(settings);
        var changedCount = 0;
        viewModel.MinecraftDirectoryChanged += (_, _) => changedCount++;

        await viewModel.ChangeMinecraftDirectoryCommand.ExecuteAsync(null);

        Assert.Equal(Path.GetFullPath(settings.MinecraftDirectory), viewModel.MinecraftDirectory);
        Assert.Equal(0, settingsService.SaveCount);
        Assert.Equal(0, changedCount);
    }

    [Fact]
    public void SelectSectionCommandUpdatesCurrentSection()
    {
        var viewModel = CreateViewModel(out _, out _);

        var launchSection = viewModel.Sections.Single(section => section.Section is SettingsPageSection.LaunchMemory);
        viewModel.SelectSectionCommand.Execute(launchSection);

        Assert.Same(launchSection, viewModel.SelectedSection);
        Assert.Equal(Strings.Settings_SectionLaunchMemory, viewModel.SectionTitle);
        Assert.True(viewModel.IsLaunchMemorySection);
        Assert.IsType<LaunchMemorySettingsViewModel>(viewModel.CurrentSectionViewModel);
        Assert.False(viewModel.IsGeneralSection);
        Assert.True(launchSection.IsSelected);
        Assert.False(viewModel.Sections.Single(section => section.Section is SettingsPageSection.General).IsSelected);
    }

    [Fact]
    public void ControlListViewModelKeepsInteractiveControlsForFutureUse()
    {
        var viewModel = CreateViewModel(out _, out _);

        Assert.IsType<ControlListSettingsViewModel>(viewModel.ControlList);
        Assert.Contains(
            viewModel.InteractiveControls,
            control => control.Title == Strings.Settings_ControlComboBox);
        Assert.Contains(
            viewModel.InteractiveControls,
            control => control.Title == Strings.Settings_ControlSwitch);
        Assert.Contains(
            viewModel.InteractiveControls,
            control => control.Title == Strings.Settings_ControlSlider);
    }

    [Fact]
    public void ThemeSectionShowsThemeOptions()
    {
        var viewModel = CreateViewModel(out _, out _);

        var themeSection = viewModel.Sections.Single(section => section.Section is SettingsPageSection.Theme);
        viewModel.SelectSectionCommand.Execute(themeSection);

        Assert.Same(themeSection, viewModel.SelectedSection);
        Assert.Equal(Strings.Settings_SectionTheme, viewModel.SectionTitle);
        Assert.True(viewModel.IsThemeSection);
        Assert.IsType<ThemeSettingsViewModel>(viewModel.CurrentSectionViewModel);
        Assert.False(viewModel.IsGeneralSection);
        Assert.False(viewModel.IsLaunchMemorySection);
        Assert.False(viewModel.IsJavaSection);
        Assert.False(viewModel.IsControlListSection);
        Assert.Equal(2, viewModel.ThemeOptions.Count);
        Assert.Equal(8, viewModel.AccentColorOptions.Count);
        Assert.True(viewModel.FollowSystemTheme);
        Assert.False(viewModel.IsThemeSelectionVisible);
        Assert.Equal(LauncherDefaults.DefaultTheme, viewModel.SelectedThemeOption?.Id);
        Assert.Equal(LauncherDefaults.DefaultAccentColor, viewModel.SelectedAccentColorOption?.Id);
        Assert.Equal(LauncherDefaults.DefaultLauncherBackgroundOpacityPercent, viewModel.LauncherBackgroundOpacityPercent);
        Assert.Equal("85%", viewModel.LauncherBackgroundOpacityText);
    }

    [Fact]
    public void InfoSectionShowsLauncherVersionAndActions()
    {
        var viewModel = CreateViewModel(out _, out var statusService, out var externalLinkService);

        var infoSection = viewModel.Sections.Single(section => section.Section is SettingsPageSection.Info);
        viewModel.SelectSectionCommand.Execute(infoSection);

        Assert.Same(infoSection, viewModel.SelectedSection);
        Assert.Equal(Strings.Settings_SectionInfo, viewModel.SectionTitle);
        Assert.True(viewModel.IsInfoSection);
        var info = Assert.IsType<InfoSettingsViewModel>(viewModel.CurrentSectionViewModel);
        Assert.Same(viewModel.Info, info);
        Assert.False(viewModel.IsGeneralSection);
        Assert.False(viewModel.IsLaunchMemorySection);
        Assert.False(viewModel.IsJavaSection);
        Assert.False(viewModel.IsThemeSection);
        Assert.False(viewModel.IsControlListSection);
        Assert.Equal("1.0.4", info.LauncherVersionText);

        info.OpenGithubRepositoryCommand.Execute(null);

        Assert.Equal(LauncherProjectLinks.GitHubRepositoryUrl, externalLinkService.LastOpenedUrl);
        Assert.Null(statusService.LastMessage);
    }

    [Fact]
    public void InfoGithubRepositoryCommandReportsFailureWhenLinkCannotOpen()
    {
        var viewModel = CreateViewModel(out _, out var statusService, out var externalLinkService);
        externalLinkService.TryOpenResult = false;

        viewModel.Info.OpenGithubRepositoryCommand.Execute(null);

        Assert.Equal(LauncherProjectLinks.GitHubRepositoryUrl, externalLinkService.LastOpenedUrl);
        Assert.Equal(Strings.Status_OpenGithubRepositoryFailed, statusService.LastMessage);
    }

    [Fact]
    public void InfoReferenceProjectsListContainsRuntimeDependenciesOnly()
    {
        var viewModel = CreateViewModel(out _, out _);
        var references = viewModel.Info.ReferenceProjects;

        Assert.Contains(references, project =>
            project.Name == "CommunityToolkit.Mvvm"
            && project.Version == "8.4.2"
            && project.Url == "https://github.com/CommunityToolkit/dotnet");
        Assert.Contains(references, project =>
            project.Name == "Microsoft.Extensions.DependencyInjection"
            && project.Version == "10.0.9"
            && project.Url == "https://github.com/dotnet/dotnet");
        Assert.Contains(references, project =>
            project.Name == "Microsoft.Extensions.DependencyInjection.Abstractions"
            && project.Version == "10.0.9"
            && project.Url == "https://github.com/dotnet/dotnet");
        Assert.Contains(references, project =>
            project.Name == "Microsoft.Extensions.Logging"
            && project.Version == "10.0.9"
            && project.Url == "https://github.com/dotnet/dotnet");
        Assert.Contains(references, project =>
            project.Name == "Microsoft.Extensions.Logging.Abstractions"
            && project.Version == "10.0.9"
            && project.Url == "https://github.com/dotnet/dotnet");
        Assert.Contains(references, project =>
            project.Name == "Serilog"
            && project.Version == "4.2.0"
            && project.Url == "https://github.com/serilog/serilog");
        Assert.Contains(references, project =>
            project.Name == "Serilog.Extensions.Logging"
            && project.Version == "8.0.0"
            && project.Url == "https://github.com/serilog/serilog-extensions-logging");
        Assert.Contains(references, project =>
            project.Name == "Serilog.Sinks.File"
            && project.Version == "6.0.0"
            && project.Url == "https://github.com/serilog/serilog-sinks-file");
        Assert.Contains(references, project =>
            project.Name == "CmlLib.Core"
            && project.Version == "4.0.6"
            && project.Url == "https://github.com/CmlLib/CmlLib.Core");
        Assert.Contains(references, project =>
            project.Name == "CmlLib.Core.Auth.Microsoft"
            && project.Version == "3.3.1"
            && project.Url == "https://github.com/CmlLib/CmlLib.Core.Auth.Microsoft");
        Assert.Contains(references, project =>
            project.Name == "SharpCompress"
            && project.Version == "0.39.0"
            && project.Url == "https://github.com/adamhathcock/sharpcompress");

        Assert.DoesNotContain(references, project => project.Name.Equals("xunit", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, project => project.Name.Equals("coverlet.collector", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, project => project.Name.Equals("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InfoOpenReferenceProjectCommandOpensProjectUrl()
    {
        var viewModel = CreateViewModel(out _, out var statusService, out var externalLinkService);
        var project = viewModel.Info.ReferenceProjects.Single(item => item.Name == "Serilog");

        viewModel.Info.OpenReferenceProjectCommand.Execute(project);

        Assert.Equal("https://github.com/serilog/serilog", externalLinkService.LastOpenedUrl);
        Assert.Null(statusService.LastMessage);
    }

    [Fact]
    public void InfoOpenReferenceProjectCommandReportsFailureWhenLinkCannotOpen()
    {
        var viewModel = CreateViewModel(out _, out var statusService, out var externalLinkService);
        externalLinkService.TryOpenResult = false;
        var project = viewModel.Info.ReferenceProjects.Single(item => item.Name == "Serilog");

        viewModel.Info.OpenReferenceProjectCommand.Execute(project);

        Assert.Equal("https://github.com/serilog/serilog", externalLinkService.LastOpenedUrl);
        Assert.Equal(Strings.Status_OpenReferenceProjectFailed, statusService.LastMessage);
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
            "1.0.4",
            new LauncherUpdateInfo(
                "1.0.1",
                "1.0.1",
                "https://example.test/releases/v1.0.1",
                "https://example.test/downloads/MineDock_Launcher_x64.exe",
                "Release notes",
                "MineDock_Launcher_x64.exe",
                LauncherUpdateAssetKind.WindowsX64Executable));

        await viewModel.Info.CheckUpdatesCommand.ExecuteAsync(null);

        Assert.Equal("1.0.4", updateService.LastCurrentVersion);
        Assert.True(viewModel.Info.IsUpdateAvailableDialogOpen);
        Assert.Equal("1.0.1", viewModel.Info.UpdateDialogVersionText);
        Assert.Equal(
            string.Format(Strings.Dialog_UpdateAvailableVersionFormat, "1.0.1"),
            viewModel.Info.UpdateDialogMessage);
    }

    [Fact]
    public async Task InfoCheckUpdatesCommandStaysExecutableWhileChecking()
    {
        var viewModel = CreateViewModel(
            out _,
            out _,
            out _,
            out var updateService);
        var pendingResult = new TaskCompletionSource<LauncherUpdateCheckResult>();
        updateService.ResultTask = pendingResult.Task;

        var checkTask = viewModel.Info.CheckUpdatesCommand.ExecuteAsync(null);

        Assert.True(viewModel.Info.IsCheckingUpdates);
        Assert.True(viewModel.Info.CheckUpdatesCommand.CanExecute(null));

        await viewModel.Info.CheckUpdatesCommand.ExecuteAsync(null);
        Assert.Equal(1, updateService.CallCount);

        pendingResult.SetResult(LauncherUpdateCheckResult.Latest("1.0.4"));
        await checkTask;

        Assert.False(viewModel.Info.IsCheckingUpdates);
    }

    [Fact]
    public async Task InfoOpenUpdateChangelogCommandOpensReleasePage()
    {
        var viewModel = CreateViewModel(
            out _,
            out _,
            out var externalLinkService,
            out var updateService);
        updateService.Result = LauncherUpdateCheckResult.Available(
            "1.0.4",
            new LauncherUpdateInfo(
                "1.0.1",
                "1.0.1",
                "https://example.test/releases/v1.0.1",
                "https://example.test/downloads/MineDock_Launcher_x64.exe",
                null,
                "MineDock_Launcher_x64.exe",
                LauncherUpdateAssetKind.WindowsX64Executable));

        await viewModel.Info.CheckUpdatesCommand.ExecuteAsync(null);
        viewModel.Info.OpenUpdateChangelogCommand.Execute(null);

        Assert.Equal("https://example.test/releases/v1.0.1", externalLinkService.LastOpenedUrl);
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
            "1.0.4",
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
    public async Task InfoConfirmUpdateCommandReportsWhenNoAutoInstallPackageExists()
    {
        var viewModel = CreateViewModel(
            out _,
            out var statusService,
            out _,
            out var updateService,
            out var selfUpdateService,
            out var exitService);
        updateService.Result = LauncherUpdateCheckResult.Available(
            "1.0.4",
            new LauncherUpdateInfo(
                "1.0.1",
                "1.0.1",
                "https://example.test/releases/v1.0.1",
                "https://example.test/releases/v1.0.1",
                null));

        await viewModel.Info.CheckUpdatesCommand.ExecuteAsync(null);
        await viewModel.Info.ConfirmUpdateCommand.ExecuteAsync(null);

        Assert.Null(selfUpdateService.LastUpdate);
        Assert.True(viewModel.Info.IsUpdateAvailableDialogOpen);
        Assert.Equal(Strings.Status_UpdateAutoInstallPackageNotFound, statusService.LastMessage);
        Assert.Equal(0, exitService.ShutdownCount);
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
            "1.0.4",
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
    public async Task InfoCheckUpdatesReportsLatestWhenNoUpdateIsAvailable()
    {
        var viewModel = CreateViewModel(
            out _,
            out var statusService,
            out _,
            out var updateService);
        updateService.Result = LauncherUpdateCheckResult.Latest("1.0.4");

        await viewModel.Info.CheckUpdatesCommand.ExecuteAsync(null);

        Assert.False(viewModel.Info.IsUpdateAvailableDialogOpen);
        Assert.Equal(Strings.Status_LauncherAlreadyLatest, statusService.LastMessage);
    }

    [Fact]
    public async Task InfoCheckUpdatesReportsFailureWhenServiceFails()
    {
        var viewModel = CreateViewModel(
            out _,
            out var statusService,
            out _,
            out var updateService);
        updateService.Result = LauncherUpdateCheckResult.Failed("1.0.4");

        await viewModel.Info.CheckUpdatesCommand.ExecuteAsync(null);

        Assert.False(viewModel.Info.IsUpdateAvailableDialogOpen);
        Assert.Equal(Strings.Status_CheckUpdatesFailed, statusService.LastMessage);
    }

    [Fact]
    public void ThemeSelectionAppearsAfterDisablingFollowSystem()
    {
        var viewModel = CreateViewModel(out _, out _);

        viewModel.FollowSystemTheme = false;
        viewModel.SelectedThemeOption = viewModel.ThemeOptions.Single(option => option.Id == "Light");

        Assert.True(viewModel.IsThemeSelectionVisible);
        Assert.Equal("Light", viewModel.SelectedThemeOption?.Id);
    }

    [Fact]
    public void AccentSelectionRemainsVisibleWhenFollowingSystemTheme()
    {
        var viewModel = CreateViewModel(out _, out _);

        Assert.True(viewModel.FollowSystemTheme);
        Assert.Equal(8, viewModel.AccentColorOptions.Count);
        Assert.Equal(LauncherDefaults.DefaultAccentColor, viewModel.SelectedAccentColorOption?.Id);
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
    public void ControlDemoActionUpdatesOnlyDemoState()
    {
        var viewModel = CreateViewModel(out var settingsService, out _);
        var initialProgress = viewModel.ControlDemoProgress;
        var initialSelection = viewModel.ControlDemoSecondaryMenuSelected;

        viewModel.RunControlDemoActionCommand.Execute(null);

        Assert.Equal(initialProgress + 20, viewModel.ControlDemoProgress);
        Assert.NotEqual(initialSelection, viewModel.ControlDemoSecondaryMenuSelected);
        Assert.Equal(Strings.Settings_ControlDemoStatusClicked, viewModel.ControlDemoStatusText);
        Assert.Equal(0, settingsService.SaveCount);
    }

    [Fact]
    public void ControlDemoSliderDoesNotSaveSettings()
    {
        var viewModel = CreateViewModel(out var settingsService, out _);

        viewModel.ControlDemoSliderValue = 72;

        Assert.Equal(72, viewModel.ControlDemoSliderValue);
        Assert.Equal(0, settingsService.SaveCount);
    }

    [Fact]
    public void JavaSelectionDefaultsToAutomatic()
    {
        var viewModel = CreateViewModel(out _, out _);

        Assert.Equal(2, viewModel.JavaSelectionOptions.Count);
        Assert.Equal(Strings.Settings_JavaSelectionAuto, viewModel.SelectedJavaSelectionOption?.Title);
        Assert.Null(viewModel.SelectedJavaRuntime);
    }

    [Fact]
    public async Task AutomaticJavaSelectionDoesNotSelectRuntimeAfterRefresh()
    {
        var javaRuntimeDiscoveryService = new FakeJavaRuntimeDiscoveryService
        {
            Runtimes =
            [
                CreateJavaRuntime(@"C:\Java\jdk-21\bin\java.exe", 21)
            ]
        };
        var viewModel = CreateViewModel(
            new LauncherSettings(),
            javaRuntimeDiscoveryService,
            out _,
            out _);

        await viewModel.RefreshJavaRuntimesCommand.ExecuteAsync(null);

        Assert.Single(viewModel.JavaRuntimes);
        Assert.Null(viewModel.SelectedJavaRuntime);
    }

    [Fact]
    public async Task SwitchingFromAutomaticToManualSelectsFirstRuntime()
    {
        var firstRuntime = CreateJavaRuntime(@"C:\Java\jdk-21\bin\java.exe", 21);
        var javaRuntimeDiscoveryService = new FakeJavaRuntimeDiscoveryService
        {
            Runtimes =
            [
                firstRuntime,
                CreateJavaRuntime(@"C:\Java\jdk-17\bin\java.exe", 17)
            ]
        };
        var viewModel = CreateViewModel(
            new LauncherSettings(),
            javaRuntimeDiscoveryService,
            out _,
            out _);

        await viewModel.RefreshJavaRuntimesCommand.ExecuteAsync(null);
        viewModel.SelectedJavaSelectionOption = viewModel.JavaSelectionOptions.Single(option => option.Id == "manual");

        Assert.Equal(firstRuntime.ExecutablePath, viewModel.SelectedJavaRuntime?.ExecutablePath);
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
    public async Task SwitchingManualJavaSelectionBackToAutomaticClearsUiSelectionOnly()
    {
        var savedPath = @"C:\Java\jdk-17\bin\java.exe";
        var settings = new LauncherSettings
        {
            JavaSelectionMode = JavaSelectionMode.Manual,
            SelectedJavaExecutablePath = savedPath
        };
        var javaRuntimeDiscoveryService = new FakeJavaRuntimeDiscoveryService
        {
            Runtimes =
            [
                CreateJavaRuntime(savedPath, 17)
            ]
        };
        var viewModel = CreateViewModel(settings, javaRuntimeDiscoveryService, out var settingsService, out _);
        viewModel.PrimeFromSettings(settings);

        await viewModel.RefreshJavaRuntimesCommand.ExecuteAsync(null);
        Assert.NotNull(viewModel.SelectedJavaRuntime);

        viewModel.SelectedJavaSelectionOption = viewModel.JavaSelectionOptions.Single(option => option.Id == "auto");

        Assert.Null(viewModel.SelectedJavaRuntime);
        await TestAsync.WaitForAsync(() =>
            settingsService.SaveCount >= 1
            && settings.JavaSelectionMode is JavaSelectionMode.Auto);
        Assert.Equal(savedPath, settings.SelectedJavaExecutablePath);
    }

    [Fact]
    public async Task ManualJavaSelectionRestoresSavedPathAfterRefresh()
    {
        var savedPath = @"C:\Java\jdk-17\bin\java.exe";
        var settings = new LauncherSettings
        {
            JavaSelectionMode = JavaSelectionMode.Manual,
            SelectedJavaExecutablePath = savedPath
        };
        var javaRuntimeDiscoveryService = new FakeJavaRuntimeDiscoveryService
        {
            Runtimes =
            [
                CreateJavaRuntime(@"C:\Java\jdk-21\bin\java.exe", 21),
                CreateJavaRuntime(savedPath, 17)
            ]
        };
        var viewModel = CreateViewModel(settings, javaRuntimeDiscoveryService, out _, out _);
        viewModel.PrimeFromSettings(settings);

        await viewModel.RefreshJavaRuntimesCommand.ExecuteAsync(null);

        Assert.Equal(savedPath, viewModel.SelectedJavaRuntime?.ExecutablePath);
    }

    [Fact]
    public async Task ManualJavaSelectionAddsSavedPathWhenMissingFromScan()
    {
        var savedPath = @"C:\Imported\jdk-21\bin\java.exe";
        var settings = new LauncherSettings
        {
            JavaSelectionMode = JavaSelectionMode.Manual,
            SelectedJavaExecutablePath = savedPath
        };
        var javaRuntimeDiscoveryService = new FakeJavaRuntimeDiscoveryService
        {
            Runtimes = [],
            ImportedRuntime = CreateJavaRuntime(savedPath, 21)
        };
        var viewModel = CreateViewModel(settings, javaRuntimeDiscoveryService, out _, out _);
        viewModel.PrimeFromSettings(settings);

        await viewModel.RefreshJavaRuntimesCommand.ExecuteAsync(null);

        Assert.Single(viewModel.JavaRuntimes);
        Assert.Equal(savedPath, viewModel.SelectedJavaRuntime?.ExecutablePath);
        Assert.Equal(savedPath, javaRuntimeDiscoveryService.LastImportedExecutablePath);
    }

    [Fact]
    public async Task RefreshJavaRuntimesShowsDiscoveredRuntimes()
    {
        var settings = new LauncherSettings
        {
            MinecraftDirectory = @"C:\Launcher\.minecraft"
        };
        var javaRuntimeDiscoveryService = new FakeJavaRuntimeDiscoveryService
        {
            Runtimes =
            [
                new JavaRuntimeInfo(
                    "Java 21",
                    "21.0.2",
                    21,
                    "x64",
                    @"C:\Program Files\Java\jdk-21\bin\java.exe",
                    @"C:\Program Files\Java\jdk-21",
                    "JAVA_HOME")
            ]
        };
        var viewModel = CreateViewModel(settings, javaRuntimeDiscoveryService, out _, out _);
        viewModel.PrimeFromSettings(settings);

        await viewModel.RefreshJavaRuntimesCommand.ExecuteAsync(null);

        Assert.Single(viewModel.JavaRuntimes);
        Assert.Equal("Java 21", viewModel.JavaRuntimes[0].DisplayName);
        Assert.Equal("21.0.2", viewModel.JavaRuntimes[0].VersionText);
        Assert.Equal(@"C:\Launcher\.minecraft", javaRuntimeDiscoveryService.LastMinecraftDirectory);
        Assert.False(viewModel.HasJavaRuntimeListMessage);
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
    public async Task ImportJavaRuntimeDoesNotDuplicateExistingRuntime()
    {
        const string executablePath = @"C:\Java\jdk-21\bin\java.exe";
        var runtime = new JavaRuntimeInfo(
            "Java 21",
            "21.0.2",
            21,
            "x64",
            executablePath,
            @"C:\Java\jdk-21",
            "ManualImport");
        var javaRuntimeDiscoveryService = new FakeJavaRuntimeDiscoveryService
        {
            ImportedRuntime = runtime
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
            out var floatingMessageService);

        await viewModel.ImportJavaRuntimeCommand.ExecuteAsync(null);
        await viewModel.ImportJavaRuntimeCommand.ExecuteAsync(null);

        Assert.Single(viewModel.JavaRuntimes);
        Assert.Equal(Strings.Status_JavaImported, statusService.LastMessage);
        Assert.Equal(Strings.Status_JavaAlreadyExists, floatingMessageService.LastMessage);
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
    public async Task InvalidDownloadSpeedLimitFallsBackToUnlimited()
    {
        var settings = new LauncherSettings
        {
            DownloadSpeedLimitMbPerSecond = 12
        };
        var viewModel = CreateViewModel(settings, out var settingsService, out _);
        viewModel.PrimeFromSettings(settings);

        viewModel.DownloadSpeedLimitMbPerSecondText = "abc";

        await TestAsync.WaitForAsync(() =>
            settingsService.SaveCount >= 1
            && settings.DownloadSpeedLimitMbPerSecond == 0
            && viewModel.DownloadSpeedLimitMbPerSecondText == string.Empty);
    }

    [Fact]
    public async Task DisablingLaunchCheckAlsoDisablesAutoRepair()
    {
        var settings = new LauncherSettings
        {
            DefaultCheckFilesBeforeLaunch = true,
            DefaultAutoRepairMissingFiles = true
        };
        var viewModel = CreateViewModel(settings, out var settingsService, out _);
        viewModel.PrimeFromSettings(settings);

        viewModel.DefaultCheckFilesBeforeLaunch = false;

        Assert.False(viewModel.DefaultAutoRepairMissingFiles);
        await TestAsync.WaitForAsync(() =>
            settingsService.SaveCount >= 1
            && !settings.DefaultCheckFilesBeforeLaunch
            && !settings.DefaultAutoRepairMissingFiles);
    }

    [Fact]
    public async Task EnablingLaunchCheckAlsoEnablesAutoRepair()
    {
        var settings = new LauncherSettings
        {
            DefaultCheckFilesBeforeLaunch = false,
            DefaultAutoRepairMissingFiles = false
        };
        var viewModel = CreateViewModel(settings, out var settingsService, out _);
        viewModel.PrimeFromSettings(settings);

        viewModel.DefaultCheckFilesBeforeLaunch = true;

        Assert.True(viewModel.DefaultAutoRepairMissingFiles);
        await TestAsync.WaitForAsync(() =>
            settingsService.SaveCount >= 1
            && settings.DefaultCheckFilesBeforeLaunch
            && settings.DefaultAutoRepairMissingFiles);
    }

    [Fact]
    public void LaunchDefaultsChangedUpdatesSharedSettingsImmediately()
    {
        var settings = new LauncherSettings
        {
            DefaultCheckFilesBeforeLaunch = true,
            DefaultAutoRepairMissingFiles = true,
            DefaultMinimizeLauncherAfterLaunch = false,
            DefaultLaunchFullScreen = false,
            DefaultMemorySettingsMode = MemorySettingsMode.Auto,
            DefaultMemoryMb = 4096
        };
        var viewModel = CreateViewModel(settings, out _, out _);
        viewModel.PrimeFromSettings(settings);
        var changedCount = 0;
        viewModel.LaunchDefaultsChanged += (_, _) => changedCount++;

        viewModel.DefaultCheckFilesBeforeLaunch = false;

        Assert.Equal(1, changedCount);
        Assert.False(settings.DefaultCheckFilesBeforeLaunch);
        Assert.False(settings.DefaultAutoRepairMissingFiles);

        viewModel.DefaultLaunchFullScreen = true;

        Assert.Equal(2, changedCount);
        Assert.True(settings.DefaultLaunchFullScreen);

        viewModel.DefaultGameArguments = "--quickPlaySingleplayer world";

        Assert.Equal(3, changedCount);
        Assert.Equal("--quickPlaySingleplayer world", settings.DefaultGameArguments);

        viewModel.DefaultWaitForPreLaunchCommand = false;

        Assert.Equal(4, changedCount);
        Assert.False(settings.DefaultWaitForPreLaunchCommand);

        viewModel.SelectedMemoryModeOption = viewModel.MemoryModeOptions.Single(option => option.Mode == MemorySettingsMode.Manual);

        Assert.Equal(5, changedCount);
        Assert.Equal(MemorySettingsMode.Manual, settings.DefaultMemorySettingsMode);

        viewModel.DefaultMemoryMb = 8192;

        Assert.Equal(6, changedCount);
        Assert.Equal(8192, settings.DefaultMemoryMb);
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

    [Fact]
    public void ChangingDownloadSourceRaisesChangedEvent()
    {
        var settings = new LauncherSettings
        {
            DownloadSourcePreference = DownloadSourcePreference.Auto
        };
        var viewModel = CreateViewModel(settings, out _, out _);
        viewModel.PrimeFromSettings(settings);
        var changedPreference = DownloadSourcePreference.Auto;
        var changedCount = 0;
        viewModel.DownloadSourceChanged += (_, e) =>
        {
            changedCount++;
            changedPreference = e.Preference;
        };

        viewModel.SelectedDownloadSourceOption = viewModel.DownloadSourceOptions.Single(option =>
            option.Preference is DownloadSourcePreference.BmclApi);

        Assert.Equal(1, changedCount);
        Assert.Equal(DownloadSourcePreference.BmclApi, changedPreference);
        Assert.Equal(DownloadSourcePreference.BmclApi, settings.DownloadSourcePreference);
    }

    [Fact]
    public void ChangingDownloadSpeedLimitRaisesChangedEvent()
    {
        var settings = new LauncherSettings
        {
            DownloadSpeedLimitMbPerSecond = 0
        };
        var viewModel = CreateViewModel(settings, out _, out _);
        viewModel.PrimeFromSettings(settings);
        var changedSpeedLimit = 0;
        var changedCount = 0;
        viewModel.DownloadSpeedLimitChanged += (_, e) =>
        {
            changedCount++;
            changedSpeedLimit = e.DownloadSpeedLimitMbPerSecond;
        };

        viewModel.DownloadSpeedLimitMbPerSecondText = "12";

        Assert.Equal(1, changedCount);
        Assert.Equal(12, changedSpeedLimit);
        Assert.Equal(12, settings.DownloadSpeedLimitMbPerSecond);
    }

    [Fact]
    public void PrimeFromSettingsDoesNotAutoSave()
    {
        var settings = new LauncherSettings();
        var viewModel = CreateViewModel(settings, out var settingsService, out _);

        viewModel.PrimeFromSettings(settings);

        Assert.Equal(0, settingsService.SaveCount);
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

    [Fact]
    public async Task AutomaticMemoryModeDisablesMemorySlider()
    {
        var settings = new LauncherSettings
        {
            DefaultMemorySettingsMode = MemorySettingsMode.Manual,
            DefaultMemoryMb = 4096
        };
        var viewModel = CreateViewModel(settings, out var settingsService, out _);
        viewModel.PrimeFromSettings(settings);

        viewModel.SelectedMemoryModeOption = viewModel.MemoryModeOptions.Single(option => option.Mode == MemorySettingsMode.Auto);

        Assert.False(viewModel.IsMemorySliderEnabled);
        Assert.True(viewModel.IsAutomaticMemorySummaryVisible);
        Assert.Equal("4.0 GB", viewModel.AutomaticMemoryText);
        await TestAsync.WaitForAsync(() =>
            settingsService.SaveCount >= 1
            && settings.DefaultMemorySettingsMode == MemorySettingsMode.Auto
            && settings.DefaultMemoryMb == 4096);
    }

    [Theory]
    [InlineData(8192, 6144)]
    [InlineData(16384, 12288)]
    [InlineData(24576, 18432)]
    [InlineData(4096, 2048)]
    [InlineData(2048, 1024)]
    public void MemorySliderMaximumUsesSystemMemoryRule(int totalMemoryMb, int expectedMaximumMb)
    {
        Assert.Equal(expectedMaximumMb, SettingsPageViewModel.CalculateMemorySliderMaximumMb(totalMemoryMb));
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
        public LauncherUpdateCheckResult Result { get; set; } = LauncherUpdateCheckResult.Latest("1.0.4");
        public Task<LauncherUpdateCheckResult>? ResultTask { get; set; }
        public string? LastCurrentVersion { get; private set; }
        public int CallCount { get; private set; }

        public Task<LauncherUpdateCheckResult> CheckForUpdatesAsync(
            string currentVersion,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastCurrentVersion = currentVersion;
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
