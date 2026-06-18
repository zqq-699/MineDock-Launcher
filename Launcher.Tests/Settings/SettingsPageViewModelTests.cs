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
        Assert.Equal(8192, viewModel.SelectedMemoryOption?.MemoryMb);
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
            DefaultMemoryMb = 4096,
            DefaultCheckFilesBeforeLaunch = true,
            DefaultAutoRepairMissingFiles = true
        };
        var viewModel = CreateViewModel(settings, out var settingsService, out var statusService);
        viewModel.PrimeFromSettings(settings);

        viewModel.SelectedMemoryOption = new SettingsMemoryOption(12288);
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
            DefaultLaunchFullScreen = false
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
            javaRuntimeDiscoveryService,
            filePickerService,
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

    private sealed class FakeFilePickerService : IFilePickerService
    {
        public string? JavaExecutablePath { get; init; }

        public string? PickMinecraftSkin()
        {
            return null;
        }

        public string? PickJavaExecutable()
        {
            return JavaExecutablePath;
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
