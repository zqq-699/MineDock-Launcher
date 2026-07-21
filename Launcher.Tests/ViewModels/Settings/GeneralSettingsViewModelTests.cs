/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Resources;
using Launcher.App.Logging;
using Launcher.App.Services;
using Launcher.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.ViewModels.Settings;

public sealed class GeneralSettingsViewModelTests
{
    [Fact]
    public void DiagnosticLoggingToggleAppliesImmediatelyAndUpdatesSettings()
    {
        var settings = new LauncherSettings();
        var status = new RecordingStatusService();
        using var persistence = CreatePersistence(settings, status);
        var controller = new RecordingLogLevelController();
        using var viewModel = new GeneralSettingsViewModel(
            persistence,
            status,
            new CallbackFilePickerService(() => null),
            new PassthroughInstanceFolderService(),
            null,
            controller,
            NullLogger.Instance);
        viewModel.Load(settings);

        viewModel.DiagnosticLoggingEnabled = true;

        Assert.True(settings.EnableDiagnosticLogging);
        Assert.True(controller.IsDiagnosticLoggingEnabled);
        Assert.Equal(1, controller.ChangeCount);
    }

    [Fact]
    public async Task DownloadStartingWhileFolderPickerIsOpenPreventsDirectorySave()
    {
        var settings = new LauncherSettings { MinecraftDirectory = "C:\\Minecraft\\old" };
        var settingsService = new TestSettingsService(settings);
        var status = new RecordingStatusService();
        using var persistence = new SettingsPersistenceCoordinator(settingsService, status, NullLogger.Instance);
        persistence.Prime(settings);
        var downloads = new DownloadTasksPageViewModel(TimeSpan.FromMinutes(1));
        var picker = new CallbackFilePickerService(() =>
        {
            downloads.BeginTask("install", "instance");
            return "C:\\Minecraft\\new";
        });
        using var viewModel = new GeneralSettingsViewModel(
            persistence,
            status,
            picker,
            new PassthroughInstanceFolderService(),
            downloads,
            null,
            NullLogger.Instance);
        viewModel.Load(settings);

        await viewModel.ChangeMinecraftDirectoryCommand.ExecuteAsync(null);

        Assert.Equal("C:\\Minecraft\\old", settings.MinecraftDirectory);
        Assert.Equal(0, settingsService.SaveCount);
        Assert.Equal(Strings.Settings_MinecraftDirectoryChangeBlockedByActiveTasks, status.LastMessage);
    }

    private static SettingsPersistenceCoordinator CreatePersistence(
        LauncherSettings settings,
        IStatusService status)
    {
        var persistence = new SettingsPersistenceCoordinator(
            new TestSettingsService(settings),
            status,
            NullLogger.Instance);
        persistence.Prime(settings);
        return persistence;
    }

    private static GeneralSettingsViewModel CreateViewModel(
        SettingsPersistenceCoordinator persistence,
        IStatusService status,
        DownloadTasksPageViewModel downloads)
        => new(
            persistence,
            status,
            new CallbackFilePickerService(() => null),
            new PassthroughInstanceFolderService(),
            downloads,
            null,
            NullLogger.Instance);

    private sealed class RecordingStatusService : IStatusService
    {
        public event Action<string>? MessageReported;

        public string? LastMessage { get; private set; }

        public void Report(string message)
        {
            LastMessage = message;
            MessageReported?.Invoke(message);
        }
    }

    private sealed class CallbackFilePickerService(Func<string?> pickFolder) : IFilePickerService
    {
        public string? PickFolder(string title, string? initialDirectory = null) => pickFolder();
        public string? PickMinecraftSkin() => null;
        public string? PickJavaExecutable() => null;
        public string? PickLocalImportFile() => null;
        public string? PickModFile() => null;
        public string? PickSaveArchive() => null;
        public string? PickResourcePackArchive() => null;
        public string? PickShaderPackArchive() => null;
        public string? PickModpackExportArchive(string defaultFileName, ModpackExportKind kind) => null;
        public string? PickLaunchDiagnosticExportArchive(string instanceName) => null;
    }

    private sealed class PassthroughInstanceFolderService : IInstanceFolderService
    {
        public bool DirectoryExists(string folderPath) => true;
        public string EnsureDirectoryExists(string folderPath) => folderPath;
        public bool TryOpen(string folderPath) => true;
        public bool TryOpenFile(string filePath) => true;
        public bool TryRevealFile(string filePath) => true;
    }

    private sealed class RecordingLogLevelController : ILauncherLogLevelController
    {
        public bool IsDiagnosticLoggingEnabled { get; private set; }
        public int ChangeCount { get; private set; }

        public void SetDiagnosticLoggingEnabled(bool enabled)
        {
            IsDiagnosticLoggingEnabled = enabled;
            ChangeCount++;
        }
    }
}
