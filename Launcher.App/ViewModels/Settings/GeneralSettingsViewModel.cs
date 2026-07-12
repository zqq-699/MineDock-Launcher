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

using System.Collections.ObjectModel;
using System.Globalization;
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
    private readonly ILogger logger;

    internal GeneralSettingsViewModel(
        SettingsPersistenceCoordinator persistence,
        IStatusService statusService,
        IFilePickerService filePickerService,
        IInstanceFolderService instanceFolderService,
        DownloadTasksPageViewModel? downloadTasksPage,
        ILogger logger)
        : base(persistence)
    {
        this.statusService = statusService;
        this.filePickerService = filePickerService;
        this.instanceFolderService = instanceFolderService;
        this.downloadTasksPage = downloadTasksPage;
        this.logger = logger;
        if (downloadTasksPage is not null)
            downloadTasksPage.ActivityChanged += DownloadTasksPage_ActivityChanged;
        DownloadSourceOptions =
        [
            new(DownloadSourcePreference.Auto, Strings.Settings_DownloadSourceAuto),
            new(DownloadSourcePreference.Official, Strings.Settings_DownloadSourceOfficial),
            new(DownloadSourcePreference.BmclApi, Strings.Settings_DownloadSourceBmclApi)
        ];
        selectedDownloadSourceOption = DownloadSourceOptions[0];
    }

    public event EventHandler<SettingsDownloadSourceChangedEventArgs>? DownloadSourceChanged;
    public event EventHandler<SettingsDownloadSpeedLimitChangedEventArgs>? DownloadSpeedLimitChanged;
    public event EventHandler<SettingsMinecraftDirectoryChangedEventArgs>? MinecraftDirectoryChanged;

    public ObservableCollection<SettingsDownloadSourceOption> DownloadSourceOptions { get; }

    public bool CanChangeMinecraftDirectory => downloadTasksPage?.HasActiveOperations != true;

    public bool IsMinecraftDirectoryChangeBlocked => !CanChangeMinecraftDirectory;

    [ObservableProperty]
    private string minecraftDirectory = string.Empty;

    [ObservableProperty]
    private string launcherLogDirectory = string.Empty;

    [ObservableProperty]
    private SettingsDownloadSourceOption? selectedDownloadSourceOption;

    [ObservableProperty]
    private string downloadSpeedLimitMbPerSecondText = string.Empty;

    public void Load(LauncherSettings settings)
    {
        LoadState(() =>
        {
            MinecraftDirectory = settings.MinecraftDirectory;
            LauncherLogDirectory = LauncherLogConfiguration.ResolveLogDirectory();
            SelectedDownloadSourceOption = DownloadSourceOptions.FirstOrDefault(option =>
                option.Preference == settings.DownloadSourcePreference) ?? DownloadSourceOptions[0];
            DownloadSpeedLimitMbPerSecondText = FormatDownloadSpeedLimit(settings.DownloadSpeedLimitMbPerSecond);
        });
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

    partial void OnSelectedDownloadSourceOptionChanged(SettingsDownloadSourceOption? value)
    {
        if (!CanPersist)
            return;
        var preference = value?.Preference ?? DownloadSourcePreference.Auto;
        Persist(settings => settings.DownloadSourcePreference = preference);
        DownloadSourceChanged?.Invoke(this, new SettingsDownloadSourceChangedEventArgs(preference));
    }

    partial void OnDownloadSpeedLimitMbPerSecondTextChanged(string value)
    {
        if (!CanPersist)
            return;
        var limit = NormalizeDownloadSpeedLimit(value);
        Persist(settings => settings.DownloadSpeedLimitMbPerSecond = limit);
        DownloadSpeedLimitChanged?.Invoke(this, new SettingsDownloadSpeedLimitChangedEventArgs(limit));
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

    private static int NormalizeDownloadSpeedLimit(string? value)
    {
        return int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Max(parsed, 0)
            : 0;
    }

    private static string FormatDownloadSpeedLimit(int value)
        => value > 0 ? value.ToString(CultureInfo.InvariantCulture) : string.Empty;
}
