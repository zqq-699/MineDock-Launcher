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
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.GameSettings;

public sealed class LocalShaderPacksViewModel : IDisposable
{
    private readonly ILocalShaderPackService service;
    private readonly IStatusService statusService;
    private readonly ILogger<LocalShaderPacksViewModel> logger;
    private readonly LocalContentRefreshCoordinator<LocalShaderPack> refreshCoordinator;

    public LocalShaderPacksViewModel(
        ILocalShaderPackService localShaderPackService,
        IStatusService statusService,
        IInstanceDirectoryMonitor instanceDirectoryMonitor,
        IUiDispatcher? uiDispatcher = null,
        ILogger<LocalShaderPacksViewModel>? logger = null)
    {
        service = localShaderPackService;
        this.statusService = statusService;
        this.logger = logger ?? NullLogger<LocalShaderPacksViewModel>.Instance;
        refreshCoordinator = new LocalContentRefreshCoordinator<LocalShaderPack>(
            instanceDirectoryMonitor,
            InstanceDirectoryKind.ShaderPacks,
            localShaderPackService.GetShaderPacksAsync,
            Apply,
            Clear,
            _ => ReportStatus(Strings.Status_LoadLocalShaderPacksFailed),
            uiDispatcher ?? ImmediateUiDispatcher.Instance,
            this.logger);
    }

    public event EventHandler? ShaderPacksChanged;

    public ObservableCollection<LocalShaderPack> ShaderPacks { get; } = [];

    public IReadOnlyList<LocalShaderPack> CurrentShaderPacks => refreshCoordinator.CurrentItems;

    public void SetSelectedInstance(GameInstance? instance) => refreshCoordinator.SetInstance(instance);

    public void SetWatcherEnabled(bool enabled) => refreshCoordinator.SetWatcherEnabled(enabled);

    public void SuspendWatcherForInstanceRename() => refreshCoordinator.SuspendForRename();

    public void ResumeWatcherAfterInstanceRename() => refreshCoordinator.ResumeAfterRename();

    public Task SetSelectedInstanceAsync(GameInstance? instance)
    {
        SetSelectedInstance(instance);
        SetWatcherEnabled(true);
        return RefreshShaderPacksAsync();
    }

    public Task<bool> RefreshShaderPacksAsync() => refreshCoordinator.RefreshAsync();

    public async Task<int> DeleteShaderPacksAsync(IEnumerable<LocalShaderPack> shaderPacks)
    {
        var failed = await LocalContentBatchExecutor.ExecuteAsync(
            shaderPacks,
            item => item.FullPath,
            item => service.DeleteAsync(item),
            (item, exception) => logger.LogWarning(exception, "Failed to delete local shader pack. Path={Path}", item.FullPath));
        await RefreshShaderPacksAsync();
        return failed;
    }

    public async Task<LocalShaderPackImportResult> ImportShaderPackAsync(string archivePath, bool reportStatus = true)
    {
        var instance = refreshCoordinator.SelectedInstance;
        if (instance is null || string.IsNullOrWhiteSpace(archivePath))
            return LocalShaderPackImportResult.Failure(LocalShaderPackImportFailureReason.UnexpectedError);
        var result = await service.ImportAsync(instance, archivePath);
        if (!result.IsSuccess)
        {
            if (reportStatus)
            {
                ReportStatus(result.FailureReason is LocalShaderPackImportFailureReason.FileNotFound
                    ? Strings.Status_LocalShaderPackImportFileNotFound
                    : Strings.Status_LocalShaderPackImportFailed);
            }
            return result;
        }
        await RefreshShaderPacksAsync();
        if (reportStatus)
            ReportStatus(Strings.Status_LocalShaderPackImported);
        return result;
    }

    public void Dispose() => refreshCoordinator.Dispose();

    private void Apply(IReadOnlyList<LocalShaderPack> items)
    {
        ShaderPacks.ReplaceWith(items);
        ShaderPacksChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Clear()
    {
        ShaderPacks.Clear();
        ShaderPacksChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReportStatus(string message) => statusService.Report(message);
}
