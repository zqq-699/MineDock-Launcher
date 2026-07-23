/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Services;
using Launcher.App.ViewModels.Settings;
using Launcher.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.ViewModels.Settings;

public sealed class JavaSettingsEditorViewModelTests
{
    private const string ZuluExecutablePath = @"C:\Program Files\Zulu\zulu-21\bin\java.exe";
    private const string TemurinExecutablePath = @"D:\Java\temurin-17\bin\java.exe";

    [Fact]
    public async Task ImportInAutomaticModeKeepsModeAndSelectionUnchangedAndSurvivesRefresh()
    {
        var discovery = new RecordingJavaRuntimeDiscoveryService
        {
            DiscoveredRuntimes = [CreateRuntime(ZuluExecutablePath, "21.0.8", 21, "ProgramFiles")],
            ImportedRuntime = CreateRuntime(TemurinExecutablePath, "17.0.16", 17, "ManualImport")
        };
        var viewModel = new JavaSettingsEditorViewModel(
            discovery,
            new RecordingStatusService(),
            new FixedJavaFilePickerService(TemurinExecutablePath),
            new RecordingFloatingMessageService(),
            () => @"C:\Games\.minecraft");
        var selectionChangedCount = 0;
        viewModel.JavaSelectionChanged += (_, _) => selectionChangedCount++;

        await viewModel.ImportJavaRuntimeAsync();

        Assert.Equal(JavaSelectionMode.Auto, viewModel.SelectedMode);
        Assert.Null(viewModel.SelectedJavaRuntime);
        Assert.Null(viewModel.SelectedExecutablePath);
        Assert.Equal(0, selectionChangedCount);
        await viewModel.RefreshJavaRuntimesAsync();

        Assert.Equal(JavaSelectionMode.Auto, viewModel.SelectedMode);
        Assert.Null(viewModel.SelectedJavaRuntime);
        Assert.Contains(viewModel.JavaRuntimes, runtime => runtime.ExecutablePath == ZuluExecutablePath);
        Assert.Contains(viewModel.JavaRuntimes, runtime => runtime.ExecutablePath == TemurinExecutablePath);
        Assert.Equal(1, discovery.ImportCallCount);
    }

    [Fact]
    public async Task ImportInManualModeSelectsNewRuntimeWithoutChangingMode()
    {
        var discovery = new RecordingJavaRuntimeDiscoveryService
        {
            DiscoveredRuntimes = [CreateRuntime(ZuluExecutablePath, "21.0.8", 21, "ProgramFiles")],
            ImportedRuntime = CreateRuntime(TemurinExecutablePath, "17.0.16", 17, "ManualImport")
        };
        var viewModel = new JavaSettingsEditorViewModel(
            discovery,
            new RecordingStatusService(),
            new FixedJavaFilePickerService(TemurinExecutablePath),
            new RecordingFloatingMessageService(),
            () => @"C:\Games\.minecraft");
        viewModel.LoadSelection(JavaSelectionMode.Manual, selectedJavaExecutablePath: null);
        await viewModel.RefreshJavaRuntimesAsync();
        var selectionChangedCount = 0;
        viewModel.JavaSelectionChanged += (_, _) => selectionChangedCount++;

        await viewModel.ImportJavaRuntimeAsync();

        Assert.Equal(JavaSelectionMode.Manual, viewModel.SelectedMode);
        Assert.Equal(TemurinExecutablePath, viewModel.SelectedExecutablePath);
        Assert.Equal(TemurinExecutablePath, viewModel.SelectedJavaRuntime?.ExecutablePath);
        Assert.Equal(1, selectionChangedCount);
    }

    [Fact]
    public async Task ImportInAutomaticModeDoesNotPersistASettingsChange()
    {
        var settings = new LauncherSettings { JavaSelectionMode = JavaSelectionMode.Auto };
        var settingsService = new TestSettingsService(settings);
        var status = new RecordingStatusService();
        using var persistence = new SettingsPersistenceCoordinator(
            settingsService,
            status,
            NullLogger.Instance);
        persistence.Prime(settings);
        var discovery = new RecordingJavaRuntimeDiscoveryService
        {
            ImportedRuntime = CreateRuntime(TemurinExecutablePath, "17.0.16", 17, "ManualImport")
        };
        var viewModel = new JavaSettingsViewModel(
            persistence,
            discovery,
            status,
            new FixedJavaFilePickerService(TemurinExecutablePath),
            new RecordingFloatingMessageService(),
            () => @"C:\Games\.minecraft");

        await viewModel.Editor.ImportJavaRuntimeAsync();
        await persistence.FlushAsync();

        Assert.Equal(JavaSelectionMode.Auto, settings.JavaSelectionMode);
        Assert.Null(settings.SelectedJavaExecutablePath);
        Assert.Equal(0, settingsService.SaveCount);
    }

    [Fact]
    public async Task ImportInManualModePersistsNewSelectedRuntime()
    {
        var settings = new LauncherSettings { JavaSelectionMode = JavaSelectionMode.Manual };
        var settingsService = new TestSettingsService(settings);
        var status = new RecordingStatusService();
        using var persistence = new SettingsPersistenceCoordinator(
            settingsService,
            status,
            NullLogger.Instance);
        persistence.Prime(settings);
        var discovery = new RecordingJavaRuntimeDiscoveryService
        {
            ImportedRuntime = CreateRuntime(TemurinExecutablePath, "17.0.16", 17, "ManualImport")
        };
        var viewModel = new JavaSettingsViewModel(
            persistence,
            discovery,
            status,
            new FixedJavaFilePickerService(TemurinExecutablePath),
            new RecordingFloatingMessageService(),
            () => @"C:\Games\.minecraft");
        viewModel.Editor.LoadSelection(JavaSelectionMode.Manual, selectedJavaExecutablePath: null);

        await viewModel.Editor.ImportJavaRuntimeAsync();
        await persistence.FlushAsync();

        Assert.Equal(JavaSelectionMode.Manual, settings.JavaSelectionMode);
        Assert.Equal(TemurinExecutablePath, settings.SelectedJavaExecutablePath);
        Assert.Equal(1, settingsService.SaveCount);
    }

    [Fact]
    public async Task MissingImportedRuntimeDisappearsOnRefresh()
    {
        var discovery = new RecordingJavaRuntimeDiscoveryService
        {
            DiscoveredRuntimes = [CreateRuntime(ZuluExecutablePath, "21.0.8", 21, "ProgramFiles")],
            ImportedRuntime = CreateRuntime(TemurinExecutablePath, "17.0.16", 17, "ManualImport")
        };
        var viewModel = new JavaSettingsEditorViewModel(
            discovery,
            new RecordingStatusService(),
            new FixedJavaFilePickerService(TemurinExecutablePath),
            new RecordingFloatingMessageService(),
            () => @"C:\Games\.minecraft");
        await viewModel.ImportJavaRuntimeAsync();
        await viewModel.RefreshJavaRuntimesAsync();
        Assert.Contains(viewModel.JavaRuntimes, runtime => runtime.ExecutablePath == TemurinExecutablePath);

        discovery.ImportedRuntimeExists = false;
        await viewModel.RefreshJavaRuntimesAsync();

        Assert.DoesNotContain(viewModel.JavaRuntimes, runtime => runtime.ExecutablePath == TemurinExecutablePath);
        Assert.Contains(viewModel.JavaRuntimes, runtime => runtime.ExecutablePath == ZuluExecutablePath);
    }

    private static JavaRuntimeInfo CreateRuntime(
        string executablePath,
        string version,
        int majorVersion,
        string source)
    {
        return new JavaRuntimeInfo(
            $"Java {majorVersion}",
            version,
            majorVersion,
            "x64",
            executablePath,
            Path.GetDirectoryName(Path.GetDirectoryName(executablePath))!,
            source);
    }

    private sealed class RecordingJavaRuntimeDiscoveryService : IJavaRuntimeDiscoveryService
    {
        public IReadOnlyList<JavaRuntimeInfo> DiscoveredRuntimes { get; init; } = [];

        public required JavaRuntimeInfo ImportedRuntime { get; init; }

        public bool ImportedRuntimeExists { get; set; } = true;

        public int ImportCallCount { get; private set; }

        private bool isImported;

        public Task<IReadOnlyList<JavaRuntimeInfo>> DiscoverAsync(
            string? minecraftDirectory,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<JavaRuntimeInfo> runtimes = isImported && ImportedRuntimeExists
                ? [.. DiscoveredRuntimes, ImportedRuntime]
                : DiscoveredRuntimes;
            return Task.FromResult(runtimes);
        }

        public Task<JavaRuntimeInfo> DiscoverExecutableAsync(
            string executablePath,
            CancellationToken cancellationToken = default)
        {
            var runtime = DiscoveredRuntimes.FirstOrDefault(item =>
                string.Equals(item.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(runtime ?? ImportedRuntime);
        }

        public Task<JavaRuntimeInfo> ImportExecutableAsync(
            string executablePath,
            CancellationToken cancellationToken = default)
        {
            ImportCallCount++;
            isImported = true;
            Assert.Equal(TemurinExecutablePath, executablePath);
            return Task.FromResult(ImportedRuntime);
        }
    }

    private sealed class FixedJavaFilePickerService(string executablePath) : IFilePickerService
    {
        public string? PickJavaExecutable() => executablePath;
        public string? PickMinecraftSkin() => null;
        public string? PickLocalImportFile() => null;
        public string? PickModFile() => null;
        public string? PickSaveArchive() => null;
        public string? PickResourcePackArchive() => null;
        public string? PickShaderPackArchive() => null;
        public string? PickModpackExportArchive(string defaultFileName, ModpackExportKind kind) => null;
        public string? PickLaunchDiagnosticExportArchive(string instanceName) => null;
        public string? PickFolder(string title, string? initialDirectory = null) => null;
    }

    private sealed class RecordingStatusService : IStatusService
    {
        public event Action<string>? MessageReported;

        public List<string> Messages { get; } = [];

        public void Report(string message)
        {
            Messages.Add(message);
            MessageReported?.Invoke(message);
        }
    }

    private sealed class RecordingFloatingMessageService : IFloatingMessageService
    {
        public event Action<string>? MessageRequested;

        public List<string> Messages { get; } = [];

        public void Show(string message)
        {
            Messages.Add(message);
            MessageRequested?.Invoke(message);
        }
    }
}
