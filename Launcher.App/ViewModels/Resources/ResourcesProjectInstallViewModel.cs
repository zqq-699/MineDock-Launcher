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
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.Utilities;
using Launcher.App.ViewModels.Download;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels.Resources;

/// <summary>
/// 编排资源版本安装的用户决策、依赖处理和结果反馈，并按资源类型选择对应安装路径。
/// </summary>
public sealed partial class ResourcesProjectInstallViewModel : ObservableObject
{
    // 对话框用 TaskCompletionSource 把按钮事件转换为可等待决策，使主安装流程仍能顺序表达。
    private readonly ResourcesOnlineProjectPageOptions options;
    private readonly IResourceProjectInstallationService? installationService;
    private readonly ResourcesRequiredDependencyPlanner dependencyPlanner;
    private readonly IFilePickerService? filePickerService;
    private readonly IFloatingMessageService? floatingMessageService;
    private readonly DownloadTasksPageViewModel? downloadTasksPage;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger? logger;
    private readonly Action<string> reportStatus;
    private readonly object installStateLock = new();
    private readonly SemaphoreSlim dependencyDialogGate = new(1, 1);
    private readonly HashSet<string> activeInstallKeys = new(StringComparer.OrdinalIgnoreCase);
    private int activeInstallCount;
    private TaskCompletionSource<RequiredDependenciesDialogChoice>? pendingDependenciesChoice;

    [ObservableProperty]
    private bool isInstalling;

    [ObservableProperty]
    private bool isFileExistsDialogOpen;

    [ObservableProperty]
    private string fileExistsDialogMessage = string.Empty;

    [ObservableProperty]
    private bool isRequiredDependenciesDialogOpen;

    internal ResourcesProjectInstallViewModel(
        ResourcesOnlineProjectPageOptions options,
        IResourceProjectInstallationService? installationService,
        ResourcesRequiredDependencyPlanner dependencyPlanner,
        IFilePickerService? filePickerService,
        IFloatingMessageService? floatingMessageService,
        DownloadTasksPageViewModel? downloadTasksPage,
        IUiDispatcher uiDispatcher,
        ILogger? logger,
        Action<string> reportStatus)
    {
        this.options = options;
        this.installationService = installationService;
        this.dependencyPlanner = dependencyPlanner;
        this.filePickerService = filePickerService;
        this.floatingMessageService = floatingMessageService;
        this.downloadTasksPage = downloadTasksPage;
        this.uiDispatcher = uiDispatcher;
        this.logger = logger;
        this.reportStatus = reportStatus;
    }

    public event EventHandler<GameInstance>? ModpackImported;

    public event EventHandler<ResourcesModpackManualDownloadsRequestedEventArgs>? ModpackManualDownloadsRequested;

    public ObservableCollection<ResourcesModDependencyRequirementItemViewModel> RequiredDependencyDialogItems { get; } = [];

    [RelayCommand]
    private void CloseFileExistsDialog()
    {
        IsFileExistsDialogOpen = false;
        FileExistsDialogMessage = string.Empty;
    }

    [RelayCommand]
    private void CancelRequiredDependenciesDialog()
    {
        ResolveDependenciesDialog(RequiredDependenciesDialogChoice.Cancel);
    }

    [RelayCommand]
    private void ContinueWithoutRequiredDependencies()
    {
        ResolveDependenciesDialog(RequiredDependenciesDialogChoice.ContinueWithoutDependencies);
    }

    [RelayCommand]
    private void AutoInstallRequiredDependencies()
    {
        ResolveDependenciesDialog(RequiredDependenciesDialogChoice.AutoInstallDependencies);
    }
}
