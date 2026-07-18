/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Logging;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Download;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels.Settings;

public sealed partial class GeneralSettingsViewModel : SettingsSectionViewModelBase, IDisposable
{
    private readonly IStatusService statusService;
    private readonly IFilePickerService filePickerService;
    private readonly IInstanceFolderService instanceFolderService;
    private readonly DownloadTasksPageViewModel? downloadTasksPage;
    private readonly ILauncherLogLevelController? logLevelController;
    private readonly ILogger logger;

    internal GeneralSettingsViewModel(
        SettingsPersistenceCoordinator persistence,
        IStatusService statusService,
        IFilePickerService filePickerService,
        IInstanceFolderService instanceFolderService,
        DownloadTasksPageViewModel? downloadTasksPage,
        ILauncherLogLevelController? logLevelController,
        ILogger logger)
        : base(persistence)
    {
        this.statusService = statusService;
        this.filePickerService = filePickerService;
        this.instanceFolderService = instanceFolderService;
        this.downloadTasksPage = downloadTasksPage;
        this.logLevelController = logLevelController;
        this.logger = logger;
        if (downloadTasksPage is not null)
            downloadTasksPage.ActivityChanged += DownloadTasksPage_ActivityChanged;
    }

    public event EventHandler<SettingsMinecraftDirectoryChangedEventArgs>? MinecraftDirectoryChanged;

    public bool CanChangeMinecraftDirectory => downloadTasksPage?.HasActiveOperations != true;

    public bool IsMinecraftDirectoryChangeBlocked => !CanChangeMinecraftDirectory;

    [ObservableProperty]
    private string minecraftDirectory = string.Empty;

    [ObservableProperty]
    private string launcherLogDirectory = string.Empty;

    [ObservableProperty]
    private bool diagnosticLoggingEnabled;

    public void Load(LauncherSettings settings)
    {
        LoadState(() =>
        {
            MinecraftDirectory = settings.MinecraftDirectory;
            LauncherLogDirectory = LauncherLogConfiguration.ResolveLogDirectory();
            DiagnosticLoggingEnabled = settings.EnableDiagnosticLogging;
        });
    }

    partial void OnDiagnosticLoggingEnabledChanged(bool value)
    {
        if (!CanPersist)
            return;

        logLevelController?.SetDiagnosticLoggingEnabled(value);
        Persist(settings => settings.EnableDiagnosticLogging = value);
        logger.LogInformation(
            "Diagnostic logging changed. Enabled={Enabled}",
            value);
    }

    [RelayCommand]
    private void OpenMinecraftDirectory()
    {
        TryOpenDirectory(MinecraftDirectory, Strings.Status_OpenMinecraftDirectoryFailed);
    }

    [RelayCommand]
    private void OpenLauncherLogDirectory()
    {
        var directory = LauncherLogConfiguration.ResolveLogDirectory();
        if (TryOpenDirectory(directory, Strings.Status_OpenLaunchLogFolderFailed))
            LauncherLogDirectory = directory;
    }

    [RelayCommand(CanExecute = nameof(CanChangeMinecraftDirectory))]
    private async Task ChangeMinecraftDirectoryAsync()
    {
        if (!CanChangeMinecraftDirectory)
        {
            statusService.Report(Strings.Settings_MinecraftDirectoryChangeBlockedByActiveTasks);
            return;
        }

        var selectedDirectory = filePickerService.PickFolder(
            Strings.FilePicker_MinecraftDirectoryTitle,
            MinecraftDirectory);
        if (string.IsNullOrWhiteSpace(selectedDirectory))
            return;

        if (!CanChangeMinecraftDirectory)
        {
            statusService.Report(Strings.Settings_MinecraftDirectoryChangeBlockedByActiveTasks);
            return;
        }

        string normalizedDirectory;
        try
        {
            normalizedDirectory = instanceFolderService.EnsureDirectoryExists(selectedDirectory);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to prepare selected Minecraft directory.");
            statusService.Report(Strings.Status_MinecraftDirectoryChangeFailed);
            return;
        }

        if (string.Equals(MinecraftDirectory, normalizedDirectory, StringComparison.OrdinalIgnoreCase))
            return;

        if (!CanChangeMinecraftDirectory)
        {
            statusService.Report(Strings.Settings_MinecraftDirectoryChangeBlockedByActiveTasks);
            return;
        }

        var previousDirectory = Settings.MinecraftDirectory;
        try
        {
            await PersistImmediatelyAsync(settings => settings.MinecraftDirectory = normalizedDirectory);
            LoadState(() => MinecraftDirectory = normalizedDirectory);
        }
        catch (Exception exception)
        {
            Settings.MinecraftDirectory = previousDirectory;
            LoadState(() => MinecraftDirectory = previousDirectory);
            logger.LogError(exception, "Failed to save selected Minecraft directory.");
            statusService.Report(Strings.Status_MinecraftDirectoryChangeFailed);
            return;
        }

        statusService.Report(Strings.Status_MinecraftDirectoryChanged);
        MinecraftDirectoryChanged?.Invoke(this, new SettingsMinecraftDirectoryChangedEventArgs(normalizedDirectory));
    }

    public void Dispose()
    {
        if (downloadTasksPage is not null)
            downloadTasksPage.ActivityChanged -= DownloadTasksPage_ActivityChanged;
    }

    private void DownloadTasksPage_ActivityChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CanChangeMinecraftDirectory));
        OnPropertyChanged(nameof(IsMinecraftDirectoryChangeBlocked));
        ChangeMinecraftDirectoryCommand.NotifyCanExecuteChanged();
    }

    private bool TryOpenDirectory(string directory, string failureMessage)
    {
        try
        {
            var prepared = instanceFolderService.EnsureDirectoryExists(directory);
            if (instanceFolderService.TryOpen(prepared))
                return true;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to open launcher directory.");
        }

        statusService.Report(failureMessage);
        return false;
    }
}
