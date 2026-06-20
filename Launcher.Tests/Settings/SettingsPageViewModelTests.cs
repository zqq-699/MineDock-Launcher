using Launcher.App.Resources;
using Launcher.App.Services;
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
            DefaultGameArguments = "--demo"
        };

        viewModel.PrimeFromSettings(settings);

        Assert.Equal(Strings.Settings_SectionGeneral, viewModel.SectionTitle);
        Assert.True(viewModel.IsGeneralSection);
        Assert.Equal(@"C:\Launcher\Data", viewModel.DataDirectory);
        Assert.Equal(@"C:\Launcher\.minecraft", viewModel.MinecraftDirectory);
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
            new FakeFloatingMessageService());
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
            new FakeFloatingMessageService());
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
            new FakeFloatingMessageService());
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
            new FakeFloatingMessageService());
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

        var launchSection = viewModel.Sections.Single(section => section.Section is SettingsPageSection.Launch);
        viewModel.SelectSectionCommand.Execute(launchSection);

        Assert.Same(launchSection, viewModel.SelectedSection);
        Assert.Equal(Strings.Settings_SectionLaunch, viewModel.SectionTitle);
        Assert.True(viewModel.IsLaunchSection);
        Assert.False(viewModel.IsGeneralSection);
        Assert.True(launchSection.IsSelected);
        Assert.False(viewModel.Sections.Single(section => section.Section is SettingsPageSection.General).IsSelected);
    }

    [Fact]
    public void ControlListSectionShowsInteractiveControls()
    {
        var viewModel = CreateViewModel(out _, out _);

        var controlListSection = viewModel.Sections.Single(section => section.Section is SettingsPageSection.ControlList);
        viewModel.SelectSectionCommand.Execute(controlListSection);

        Assert.Same(controlListSection, viewModel.SelectedSection);
        Assert.Equal(Strings.Settings_SectionControlList, viewModel.SectionTitle);
        Assert.True(viewModel.IsControlListSection);
        Assert.False(viewModel.IsGeneralSection);
        Assert.False(viewModel.IsLaunchSection);
        Assert.False(viewModel.IsJavaMemorySection);
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
        LauncherSettings settings,
        out TestSettingsService settingsService,
        out FakeStatusService statusService)
    {
        return CreateViewModel(settings, new FakeJavaRuntimeDiscoveryService(), out settingsService, out statusService);
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
        settingsService = new TestSettingsService(settings);
        statusService = new FakeStatusService();
        floatingMessageService = new FakeFloatingMessageService();
        return new SettingsPageViewModel(
            settingsService,
            statusService,
            new FakeSystemMemoryService(),
            javaRuntimeDiscoveryService,
            filePickerService,
            new FakeInstanceFolderService(),
            floatingMessageService);
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

        public string? PickFolder(string title, string? initialDirectory = null)
        {
            return FolderPath;
        }
    }

    private sealed class FakeInstanceFolderService : IInstanceFolderService
    {
        public string? LastOpenedPath { get; private set; }

        public bool TryOpen(string folderPath)
        {
            LastOpenedPath = folderPath;
            return true;
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
