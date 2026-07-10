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
    private readonly ILocalResourcePackService localResourcePackService;
    private readonly IStatusService statusService;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger<LocalResourcePacksViewModel> logger;
    private readonly InstanceContentRefreshWatcher contentWatcher;
    private CancellationTokenSource? refreshCancellationTokenSource;
    private GameInstance? selectedInstance;
    private IReadOnlyList<LocalResourcePack> currentResourcePacks = Array.Empty<LocalResourcePack>();
    private int resourcePackRefreshVersion;

    public LocalResourcePacksViewModel(
        ILocalResourcePackService localResourcePackService,
        IStatusService statusService,
        IInstanceDirectoryMonitor instanceDirectoryMonitor,
        IUiDispatcher? uiDispatcher = null,
        ILogger<LocalResourcePacksViewModel>? logger = null)
    {
        this.localResourcePackService = localResourcePackService;
        this.statusService = statusService;
        this.uiDispatcher = uiDispatcher ?? ImmediateUiDispatcher.Instance;
        this.logger = logger ?? NullLogger<LocalResourcePacksViewModel>.Instance;
        contentWatcher = new InstanceContentRefreshWatcher(
            instanceDirectoryMonitor,
            InstanceDirectoryKind.ResourcePacks,
            RefreshResourcePacksAsync,
            _ => this.uiDispatcher.Post(() => ReportStatus(Strings.Status_LoadLocalResourcePacksFailed)),
            this.logger);
    }

    public event EventHandler? ResourcePacksChanged;

    public ObservableCollection<LocalResourcePack> ResourcePacks { get; } = [];

    public IReadOnlyList<LocalResourcePack> CurrentResourcePacks => currentResourcePacks;

    public void SetSelectedInstance(GameInstance? instance)
    {
        selectedInstance = instance;
        Interlocked.Increment(ref resourcePackRefreshVersion);
        CancelRefresh();
        contentWatcher.SetInstance(instance);
        ClearResourcePacks();
        logger.LogInformation(
            "Selected instance changed for local resource packs view. InstanceId={InstanceId}",
            instance?.Id ?? "<none>");
    }

    public void SetWatcherEnabled(bool enabled)
    {
        contentWatcher.SetEnabled(enabled);
    }

    public void SuspendWatcherForInstanceRename()
    {
        contentWatcher.Suspend();
        CancelRefresh();
    }

    public void ResumeWatcherAfterInstanceRename()
    {
        contentWatcher.Resume();
    }

    public Task SetSelectedInstanceAsync(GameInstance? instance)
    {
        SetSelectedInstance(instance);
        SetWatcherEnabled(true);
        return RefreshResourcePacksAsync();
    }

    public async Task RefreshResourcePacksAsync()
    {
        var refreshVersion = Interlocked.Increment(ref resourcePackRefreshVersion);
        var refreshCts = ReplaceRefreshCancellationTokenSource();
        var instance = selectedInstance;

        if (instance is null)
        {
            ClearResourcePacks();
            logger.LogInformation("Local resource packs view cleared because no instance is selected.");
            return;
        }

        IReadOnlyList<LocalResourcePack> loadedResourcePacks;
        try
        {
            loadedResourcePacks = await localResourcePackService.GetResourcePacksAsync(instance, refreshCts.Token);
        }
        catch (OperationCanceledException) when (refreshCts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to load local resource packs. InstanceId={InstanceId}",
                instance.Id);
            throw;
        }
        finally
        {
            ReleaseRefreshCancellationTokenSource(refreshCts);
        }

        if (refreshVersion != resourcePackRefreshVersion
            || !string.Equals(instance.Id, selectedInstance?.Id, StringComparison.Ordinal))
        {
            return;
        }

        uiDispatcher.Invoke(() =>
        {
            currentResourcePacks = loadedResourcePacks;
            ResourcePacks.ReplaceWith(loadedResourcePacks);
            ResourcePacksChanged?.Invoke(this, EventArgs.Empty);
        });
        logger.LogInformation(
            "Local resource packs view refreshed. InstanceId={InstanceId} Count={ResourcePackCount}",
            instance.Id,
            ResourcePacks.Count);
    }

    public async Task<int> DeleteResourcePacksAsync(IEnumerable<LocalResourcePack> resourcePacks)
    {
        ArgumentNullException.ThrowIfNull(resourcePacks);

        var failedCount = 0;
        foreach (var resourcePack in resourcePacks.DistinctBy(resourcePack => resourcePack.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                await localResourcePackService.DeleteAsync(resourcePack);
            }
            catch (Exception exception)
            {
                failedCount++;
                logger.LogWarning(
                    exception,
                    "Failed to delete local resource pack. Path={Path}",
                    resourcePack.FullPath);
            }
        }

        await RefreshResourcePacksAsync();
        return failedCount;
    }

    public async Task<LocalResourcePackImportResult> ImportResourcePackAsync(string archivePath, bool reportStatus = true)
    {
        if (selectedInstance is null || string.IsNullOrWhiteSpace(archivePath))
            return LocalResourcePackImportResult.Failure(LocalResourcePackImportFailureReason.UnexpectedError);

        var result = await localResourcePackService.ImportAsync(selectedInstance, archivePath);
        if (!result.IsSuccess)
        {
            switch (result.FailureReason)
            {
                case LocalResourcePackImportFailureReason.FileNotFound:
                    if (reportStatus)
                        ReportStatus(Strings.Status_LocalResourcePackImportFileNotFound);
                    break;
                case LocalResourcePackImportFailureReason.UnexpectedError:
                    if (reportStatus)
                        ReportStatus(Strings.Status_LocalResourcePackImportFailed);
                    break;
            }

            return result;
        }

        await RefreshResourcePacksAsync();
        if (reportStatus)
            ReportStatus(Strings.Status_LocalResourcePackImported);
        return result;
    }

    public void Dispose()
    {
        contentWatcher.Dispose();
        CancelRefresh();
    }

    private void ReportStatus(string message)
    {
        statusService.Report(message);
    }

    private void ClearResourcePacks()
    {
        uiDispatcher.Invoke(() =>
        {
            currentResourcePacks = Array.Empty<LocalResourcePack>();
            ResourcePacks.Clear();
            ResourcePacksChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void CancelRefresh()
    {
        refreshCancellationTokenSource?.Cancel();
        refreshCancellationTokenSource?.Dispose();
        refreshCancellationTokenSource = null;
    }

    private CancellationTokenSource ReplaceRefreshCancellationTokenSource()
    {
        var next = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref refreshCancellationTokenSource, next);
        previous?.Cancel();
        previous?.Dispose();
        return next;
    }

    private void ReleaseRefreshCancellationTokenSource(CancellationTokenSource refreshCts)
    {
        var current = Interlocked.CompareExchange(ref refreshCancellationTokenSource, null, refreshCts);
        if (ReferenceEquals(current, refreshCts))
            refreshCts.Dispose();
    }
}
