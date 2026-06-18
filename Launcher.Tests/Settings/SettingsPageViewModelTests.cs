using Launcher.App.Resources;
using Launcher.App.Services;
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
        settingsService = new TestSettingsService(settings);
        statusService = new FakeStatusService();
        return new SettingsPageViewModel(settingsService, statusService);
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

}
