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

public sealed class LocalResourcePacksViewModel : IDisposable
{
    private readonly ILocalResourcePackService service;
    private readonly IStatusService statusService;
    private readonly ILogger<LocalResourcePacksViewModel> logger;
    private readonly LocalContentRefreshCoordinator<LocalResourcePack> refreshCoordinator;

    public LocalResourcePacksViewModel(
        ILocalResourcePackService localResourcePackService,
        IStatusService statusService,
        IInstanceDirectoryMonitor instanceDirectoryMonitor,
        IUiDispatcher? uiDispatcher = null,
        ILogger<LocalResourcePacksViewModel>? logger = null)
    {
        service = localResourcePackService;
        this.statusService = statusService;
        this.logger = logger ?? NullLogger<LocalResourcePacksViewModel>.Instance;
        refreshCoordinator = new LocalContentRefreshCoordinator<LocalResourcePack>(
            instanceDirectoryMonitor,
            InstanceDirectoryKind.ResourcePacks,
            localResourcePackService.GetResourcePacksAsync,
            Apply,
            Clear,
            _ => ReportStatus(Strings.Status_LoadLocalResourcePacksFailed),
            uiDispatcher ?? ImmediateUiDispatcher.Instance,
            this.logger);
    }

    public event EventHandler? ResourcePacksChanged;

    public ObservableCollection<LocalResourcePack> ResourcePacks { get; } = [];

    public IReadOnlyList<LocalResourcePack> CurrentResourcePacks => refreshCoordinator.CurrentItems;

    public void SetSelectedInstance(GameInstance? instance) => refreshCoordinator.SetInstance(instance);

    public void SetWatcherEnabled(bool enabled) => refreshCoordinator.SetWatcherEnabled(enabled);

    public void SuspendWatcherForInstanceRename() => refreshCoordinator.SuspendForRename();

    public void ResumeWatcherAfterInstanceRename() => refreshCoordinator.ResumeAfterRename();

    public Task SetSelectedInstanceAsync(GameInstance? instance)
    {
        SetSelectedInstance(instance);
        SetWatcherEnabled(true);
        return RefreshResourcePacksAsync();
    }

    public Task<bool> RefreshResourcePacksAsync() => refreshCoordinator.RefreshAsync();

    public async Task<int> DeleteResourcePacksAsync(IEnumerable<LocalResourcePack> resourcePacks)
    {
        var failed = await LocalContentBatchExecutor.ExecuteAsync(
            resourcePacks,
            item => item.FullPath,
            item => service.DeleteAsync(item),
            (item, exception) => logger.LogWarning(exception, "Failed to delete local resource pack. Path={Path}", item.FullPath));
        await RefreshResourcePacksAsync();
        return failed;
    }

    public async Task<LocalResourcePackImportResult> ImportResourcePackAsync(string archivePath, bool reportStatus = true)
    {
        var instance = refreshCoordinator.SelectedInstance;
        if (instance is null || string.IsNullOrWhiteSpace(archivePath))
            return LocalResourcePackImportResult.Failure(LocalResourcePackImportFailureReason.UnexpectedError);
        var result = await service.ImportAsync(instance, archivePath);
        if (!result.IsSuccess)
        {
            if (reportStatus)
            {
                ReportStatus(result.FailureReason is LocalResourcePackImportFailureReason.FileNotFound
                    ? Strings.Status_LocalResourcePackImportFileNotFound
                    : Strings.Status_LocalResourcePackImportFailed);
            }
            return result;
        }
        await RefreshResourcePacksAsync();
        if (reportStatus)
            ReportStatus(Strings.Status_LocalResourcePackImported);
        return result;
    }

    public void Dispose() => refreshCoordinator.Dispose();

    private void Apply(IReadOnlyList<LocalResourcePack> items)
    {
        ResourcePacks.ReplaceWith(items);
        ResourcePacksChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Clear()
    {
        ResourcePacks.Clear();
        ResourcePacksChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReportStatus(string message) => statusService.Report(message);
}
