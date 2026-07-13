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

/// <summary>
/// 展示启动器版本与参考项目，并协调手动/启动时更新检查及自更新确认流程。
/// </summary>
public sealed partial class InfoSettingsViewModel : SettingsSectionViewModelBase
{
    // 检查更新与执行更新是两个独立阶段：前者可静默，后者必须经过用户确认。
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

    internal InfoSettingsViewModel(
        SettingsPersistenceCoordinator persistence,
        IStatusService statusService,
        IFloatingMessageService floatingMessageService,
        IExternalLinkService externalLinkService,
        ILauncherUpdateService launcherUpdateService,
        ILauncherSelfUpdateService launcherSelfUpdateService,
        IApplicationExitService applicationExitService,
        ILogger<InfoSettingsViewModel>? logger = null)
        : base(persistence)
    {
        this.statusService = statusService;
        this.floatingMessageService = floatingMessageService;
        this.externalLinkService = externalLinkService;
        this.launcherUpdateService = launcherUpdateService;
        this.launcherSelfUpdateService = launcherSelfUpdateService;
        this.applicationExitService = applicationExitService;
        this.logger = logger ?? NullLogger<InfoSettingsViewModel>.Instance;
        LauncherVersionText = ResolveLauncherVersion();
        UpdateChannelOptions =
        [
            new(LauncherUpdateChannel.Release, Strings.Settings_UpdateChannelReleaseTitle),
            new(LauncherUpdateChannel.Beta, Strings.Settings_UpdateChannelBetaTitle)
        ];
        selectedUpdateChannelOption = UpdateChannelOptions[0];
    }

    public string LauncherVersionText { get; }

    public IReadOnlyList<InfoReferenceProjectItem> ReferenceProjects => RuntimeReferenceProjects;

    public ObservableCollection<SettingsUpdateChannelOption> UpdateChannelOptions { get; }

    [ObservableProperty]
    private SettingsUpdateChannelOption? selectedUpdateChannelOption;

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

    public void Load(LauncherSettings settings)
    {
        // 这里只读取更新偏好和当前版本，不在设置页加载时自动发起网络请求。
        LoadState(() => SelectedUpdateChannelOption = UpdateChannelOptions.FirstOrDefault(option =>
            option.Channel == settings.UpdateChannel) ?? UpdateChannelOptions[0]);
    }

    public string CheckUpdatesButtonText => IsCheckingUpdates
        ? Strings.Status_CheckingUpdates
        : Strings.Settings_CheckUpdatesButton;

    public string ConfirmUpdateButtonText => IsStartingUpdate
        ? Strings.Status_DownloadingLauncherUpdate
        : Strings.Dialog_UpdateButton;
}
