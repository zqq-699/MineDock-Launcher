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

using System.Reflection;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.Settings;

public sealed partial class InfoSettingsViewModel : SettingsSectionViewModelBase
{
    private readonly IStatusService statusService;
    private readonly IFloatingMessageService floatingMessageService;
    private readonly IExternalLinkService externalLinkService;
    private readonly ILauncherUpdateService launcherUpdateService;
    private readonly ILauncherSelfUpdateService launcherSelfUpdateService;
    private readonly IApplicationExitService applicationExitService;
    private readonly ILogger<InfoSettingsViewModel> logger;
    private LauncherUpdateInfo? availableUpdate;
    private string? updateDialogReleasePageUrl;
    private string? updateDialogDownloadUrl;
    private bool isUpdateCheckRunning;
    private static readonly ReadOnlyCollection<InfoReferenceProjectItem> RuntimeReferenceProjects = new(
    [
        new InfoReferenceProjectItem(
            "CommunityToolkit.Mvvm",
            "8.4.2",
            "https://github.com/CommunityToolkit/dotnet",
            "Copyright (c) .NET Foundation and Contributors. All rights reserved.",
            "MIT License"),
        new InfoReferenceProjectItem(
            "Microsoft.Extensions.DependencyInjection",
            "10.0.9",
            "https://github.com/dotnet/dotnet",
            "Copyright (c) Microsoft Corporation. All rights reserved.",
            "MIT License"),
        new InfoReferenceProjectItem(
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "10.0.9",
            "https://github.com/dotnet/dotnet",
            "Copyright (c) Microsoft Corporation. All rights reserved.",
            "MIT License"),
        new InfoReferenceProjectItem(
            "Microsoft.Extensions.Logging",
            "10.0.9",
            "https://github.com/dotnet/dotnet",
            "Copyright (c) Microsoft Corporation. All rights reserved.",
            "MIT License"),
        new InfoReferenceProjectItem(
            "Microsoft.Extensions.Logging.Abstractions",
            "10.0.9",
            "https://github.com/dotnet/dotnet",
            "Copyright (c) Microsoft Corporation. All rights reserved.",
            "MIT License"),
        new InfoReferenceProjectItem(
            "Serilog",
            "4.2.0",
            "https://github.com/serilog/serilog",
            "Copyright (c) Serilog Contributors",
            "Apache-2.0 License"),
        new InfoReferenceProjectItem(
            "Serilog.Extensions.Logging",
            "8.0.0",
            "https://github.com/serilog/serilog-extensions-logging",
            "Copyright (c) Microsoft, Serilog Contributors",
            "Apache-2.0 License"),
        new InfoReferenceProjectItem(
            "Serilog.Sinks.File",
            "6.0.0",
            "https://github.com/serilog/serilog-sinks-file",
            "Copyright (c) Serilog Contributors",
            "Apache-2.0 License"),
        new InfoReferenceProjectItem(
            "CmlLib.Core",
            "4.0.6",
            "https://github.com/CmlLib/CmlLib.Core",
            "Copyright (c) 2023 AlphaBs",
            "MIT License"),
        new InfoReferenceProjectItem(
            "CmlLib.Core.Auth.Microsoft",
            "3.3.1",
            "https://github.com/CmlLib/CmlLib.Core.Auth.Microsoft",
            "Copyright (c) 2023 AlphaBs",
            "MIT License"),
        new InfoReferenceProjectItem(
            "SharpCompress",
            "0.39.0",
            "https://github.com/adamhathcock/sharpcompress",
            "Copyright (c) 2025 Adam Hathcock",
            "MIT License"),
        new InfoReferenceProjectItem(
            "IconPark",
            "1.0.0",
            "https://github.com/bytedance/IconPark",
            "Copyright 2019-present Bytedance Inc.",
            "Apache-2.0 License")
    ]);

    public InfoSettingsViewModel(
        SettingsPageViewModel parent,
        IStatusService statusService,
        IFloatingMessageService floatingMessageService,
        IExternalLinkService externalLinkService,
        ILauncherUpdateService launcherUpdateService,
        ILauncherSelfUpdateService launcherSelfUpdateService,
        IApplicationExitService applicationExitService,
        ILogger<InfoSettingsViewModel>? logger = null)
        : base(parent)
    {
        this.statusService = statusService;
        this.floatingMessageService = floatingMessageService;
        this.externalLinkService = externalLinkService;
        this.launcherUpdateService = launcherUpdateService;
        this.launcherSelfUpdateService = launcherSelfUpdateService;
        this.applicationExitService = applicationExitService;
        this.logger = logger ?? NullLogger<InfoSettingsViewModel>.Instance;
        LauncherVersionText = ResolveLauncherVersion();
    }

    public string LauncherVersionText { get; }

    public IReadOnlyList<InfoReferenceProjectItem> ReferenceProjects => RuntimeReferenceProjects;

    [ObservableProperty]
    private bool isUpdateAvailableDialogOpen;

    [ObservableProperty]
    private string updateDialogVersionText = string.Empty;

    [ObservableProperty]
    private string updateDialogMessage = string.Empty;

    [ObservableProperty]
    private bool isCheckingUpdates;

    [ObservableProperty]
    private bool isStartingUpdate;

    public string CheckUpdatesButtonText => IsCheckingUpdates
        ? Strings.Status_CheckingUpdates
        : Strings.Settings_CheckUpdatesButton;

    public string ConfirmUpdateButtonText => IsStartingUpdate
        ? Strings.Status_DownloadingLauncherUpdate
        : Strings.Dialog_UpdateButton;

    [RelayCommand]
    private void OpenGithubRepository()
    {
        try
        {
            if (!externalLinkService.TryOpen(LauncherProjectLinks.GitHubRepositoryUrl))
                statusService.Report(Strings.Status_OpenGithubRepositoryFailed);
        }
        catch (Exception)
        {
            statusService.Report(Strings.Status_OpenGithubRepositoryFailed);
        }
    }

    [RelayCommand]
    private void OpenReferenceProject(InfoReferenceProjectItem? project)
    {
        if (project is null)
            return;

        try
        {
            if (!externalLinkService.TryOpen(project.Url))
                ReportVisibleStatus(Strings.Status_OpenReferenceProjectFailed);
        }
        catch (Exception)
        {
            ReportVisibleStatus(Strings.Status_OpenReferenceProjectFailed);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCheckUpdates), AllowConcurrentExecutions = true)]
    private async Task CheckUpdatesAsync()
    {
        await CheckUpdatesCoreAsync(UpdateCheckPresentation.Manual);
    }

    public Task CheckUpdatesOnStartupAsync()
    {
        return CheckUpdatesCoreAsync(UpdateCheckPresentation.StartupSilent);
    }

    [RelayCommand]
    private void OpenUpdateChangelog()
    {
        if (!TryOpenUpdateUrl(updateDialogReleasePageUrl))
            ReportVisibleStatus(Strings.Status_OpenUpdatePageFailed);
    }

    [RelayCommand]
    private void CancelUpdateDialog()
    {
        IsUpdateAvailableDialogOpen = false;
    }

    [RelayCommand(CanExecute = nameof(CanConfirmUpdate))]
    private async Task ConfirmUpdateAsync()
    {
        if (IsStartingUpdate)
            return;

        if (availableUpdate is null)
        {
            ReportVisibleStatus(Strings.Status_OpenUpdatePageFailed);
            return;
        }

        if (!availableUpdate.CanAutoInstall)
        {
            ReportVisibleStatus(Strings.Status_UpdateAutoInstallPackageNotFound);
            return;
        }

        IsStartingUpdate = true;
        ReportStatus(Strings.Status_DownloadingLauncherUpdate);
        try
        {
            var result = await launcherSelfUpdateService.StartUpdateAsync(availableUpdate);
            if (!result.Succeeded)
            {
                ReportVisibleStatus(Strings.Status_LauncherUpdateStartFailed);
                return;
            }

            IsUpdateAvailableDialogOpen = false;
            ReportVisibleStatus(Strings.Status_LauncherUpdateRestarting);
            applicationExitService.Shutdown();
        }
        catch (Exception)
        {
            ReportVisibleStatus(Strings.Status_LauncherUpdateStartFailed);
        }
        finally
        {
            IsStartingUpdate = false;
        }
    }

    private bool CanCheckUpdates()
    {
        return !IsStartingUpdate && !isUpdateCheckRunning;
    }

    private bool CanConfirmUpdate()
    {
        return !IsStartingUpdate;
    }

    partial void OnIsCheckingUpdatesChanged(bool value)
    {
        OnPropertyChanged(nameof(CheckUpdatesButtonText));
    }

    partial void OnIsStartingUpdateChanged(bool value)
    {
        OnPropertyChanged(nameof(ConfirmUpdateButtonText));
        CheckUpdatesCommand.NotifyCanExecuteChanged();
        ConfirmUpdateCommand.NotifyCanExecuteChanged();
    }

    private async Task CheckUpdatesCoreAsync(UpdateCheckPresentation presentation)
    {
        if (isUpdateCheckRunning)
            return;

        var channel = Parent.SelectedUpdateChannelOption?.Channel ?? LauncherDefaults.DefaultUpdateChannel;
        isUpdateCheckRunning = true;
        CheckUpdatesCommand.NotifyCanExecuteChanged();
        if (presentation is UpdateCheckPresentation.Manual)
        {
            IsCheckingUpdates = true;
            statusService.Report(Strings.Status_CheckingUpdates);
        }
        else
        {
            logger.LogInformation(
                "Startup launcher update check started. CurrentVersion={CurrentVersion} Channel={Channel}",
                LauncherVersionText,
                channel);
        }

        try
        {
            LauncherUpdateCheckResult result;
            try
            {
                result = await launcherUpdateService.CheckForUpdatesAsync(LauncherVersionText, channel);
            }
            catch (Exception exception)
            {
                if (presentation is UpdateCheckPresentation.Manual)
                {
                    ReportVisibleStatus(Strings.Status_CheckUpdatesFailed);
                }
                else
                {
                    logger.LogWarning(
                        exception,
                        "Startup launcher update check threw an exception. CurrentVersion={CurrentVersion} Channel={Channel}",
                        LauncherVersionText,
                        channel);
                }

                return;
            }

            if (result.IsFailed)
            {
                if (presentation is UpdateCheckPresentation.Manual)
                {
                    ReportVisibleStatus(Strings.Status_CheckUpdatesFailed);
                }
                else
                {
                    logger.LogWarning(
                        "Startup launcher update check failed. CurrentVersion={CurrentVersion} Channel={Channel} Error={Error}",
                        LauncherVersionText,
                        channel,
                        string.IsNullOrWhiteSpace(result.ErrorMessage) ? "<none>" : result.ErrorMessage);
                }

                return;
            }

            if (!result.IsUpdateAvailable || result.Update is null)
            {
                if (presentation is UpdateCheckPresentation.Manual)
                {
                    ReportVisibleStatus(Strings.Status_LauncherAlreadyLatest);
                }
                else
                {
                    logger.LogInformation(
                        "Startup launcher update check completed. No update available. CurrentVersion={CurrentVersion} Channel={Channel}",
                        LauncherVersionText,
                        channel);
                }

                return;
            }

            if (presentation is UpdateCheckPresentation.StartupSilent)
            {
                logger.LogInformation(
                    "Startup launcher update check found an update. CurrentVersion={CurrentVersion} Channel={Channel} UpdateVersion={UpdateVersion}",
                    LauncherVersionText,
                    channel,
                    result.Update.DisplayVersion);
            }

            ShowUpdateAvailableDialog(result.Update);
        }
        finally
        {
            if (presentation is UpdateCheckPresentation.Manual)
                IsCheckingUpdates = false;

            isUpdateCheckRunning = false;
            CheckUpdatesCommand.NotifyCanExecuteChanged();
        }
    }

    private void ShowUpdateAvailableDialog(LauncherUpdateInfo update)
    {
        availableUpdate = update;
        UpdateDialogVersionText = update.DisplayVersion;
        UpdateDialogMessage = string.Format(Strings.Dialog_UpdateAvailableVersionFormat, update.DisplayVersion);
        updateDialogReleasePageUrl = update.ReleasePageUrl;
        updateDialogDownloadUrl = string.IsNullOrWhiteSpace(update.DownloadUrl)
            ? update.ReleasePageUrl
            : update.DownloadUrl;
        IsUpdateAvailableDialogOpen = true;
    }

    private void ReportStatus(string message)
    {
        statusService.Report(message);
    }

    private void ReportVisibleStatus(string message)
    {
        statusService.Report(message);
        floatingMessageService.Show(message);
    }

    private bool TryOpenUpdateUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        try
        {
            return externalLinkService.TryOpen(url);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string ResolveLauncherVersion()
    {
        var assembly = typeof(InfoSettingsViewModel).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
            return informationalVersion.Trim();

        var assemblyVersion = assembly.GetName().Version?.ToString();
        return string.IsNullOrWhiteSpace(assemblyVersion)
            ? Strings.Settings_LauncherVersionUnknown
            : assemblyVersion;
    }

    private enum UpdateCheckPresentation
    {
        Manual,
        StartupSilent
    }
}

public sealed record InfoReferenceProjectItem(
    string Name,
    string Version,
    string Url,
    string CopyrightNotice,
    string LicenseText);
