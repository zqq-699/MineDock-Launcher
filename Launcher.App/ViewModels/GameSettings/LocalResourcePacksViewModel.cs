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
using System.IO;
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
    private CancellationTokenSource? refreshCancellationTokenSource;
    private FileSystemWatcher? resourcePacksWatcher;
    private CancellationTokenSource? watcherRefreshCancellationTokenSource;
    private GameInstance? selectedInstance;
    private IReadOnlyList<LocalResourcePack> currentResourcePacks = Array.Empty<LocalResourcePack>();
    private string? watchedResourcePacksDirectory;
    private bool watcherEnabled;
    private bool watcherSuspendedForRename;
    private int resourcePackRefreshVersion;

    public LocalResourcePacksViewModel(
        ILocalResourcePackService localResourcePackService,
        IStatusService statusService,
        IUiDispatcher? uiDispatcher = null,
        ILogger<LocalResourcePacksViewModel>? logger = null)
    {
        this.localResourcePackService = localResourcePackService;
        this.statusService = statusService;
        this.uiDispatcher = uiDispatcher ?? ImmediateUiDispatcher.Instance;
        this.logger = logger ?? NullLogger<LocalResourcePacksViewModel>.Instance;
    }

    public event EventHandler? ResourcePacksChanged;

    public ObservableCollection<LocalResourcePack> ResourcePacks { get; } = [];

    public IReadOnlyList<LocalResourcePack> CurrentResourcePacks => currentResourcePacks;

    public void SetSelectedInstance(GameInstance? instance)
    {
        selectedInstance = instance;
        Interlocked.Increment(ref resourcePackRefreshVersion);
        CancelRefresh();
        ResetWatcher();
        ClearResourcePacks();
        logger.LogInformation(
            "Selected instance changed for local resource packs view. InstanceId={InstanceId}",
            instance?.Id ?? "<none>");
    }

    public void SetWatcherEnabled(bool enabled)
    {
        watcherEnabled = enabled;
        ResetWatcher();
    }

    public void SuspendWatcherForInstanceRename()
    {
        watcherSuspendedForRename = true;
        ResetWatcher();
        CancelRefresh();
    }

    public void ResumeWatcherAfterInstanceRename()
    {
        if (!watcherSuspendedForRename)
            return;

        watcherSuspendedForRename = false;
        ResetWatcher();
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
        resourcePacksWatcher?.Dispose();
        resourcePacksWatcher = null;
        CancelRefresh();
        watcherRefreshCancellationTokenSource?.Cancel();
        watcherRefreshCancellationTokenSource?.Dispose();
        watcherRefreshCancellationTokenSource = null;
    }

    private void ResetWatcher()
    {
        resourcePacksWatcher?.Dispose();
        resourcePacksWatcher = null;
        watcherRefreshCancellationTokenSource?.Cancel();
        watcherRefreshCancellationTokenSource?.Dispose();
        watcherRefreshCancellationTokenSource = null;
        watchedResourcePacksDirectory = null;

        if (!watcherEnabled || watcherSuspendedForRename)
            return;

        var instance = selectedInstance;
        if (instance is null || string.IsNullOrWhiteSpace(instance.InstanceDirectory))
            return;

        watchedResourcePacksDirectory = Path.Combine(instance.InstanceDirectory, "resourcepacks");
        Directory.CreateDirectory(watchedResourcePacksDirectory);

        try
        {
            resourcePacksWatcher = new FileSystemWatcher(watchedResourcePacksDirectory, "*")
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.DirectoryName
                    | NotifyFilters.FileName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size
                    | NotifyFilters.CreationTime
            };
            resourcePacksWatcher.Changed += ResourcePacksWatcher_StateChanged;
            resourcePacksWatcher.Created += ResourcePacksWatcher_StateChanged;
            resourcePacksWatcher.Deleted += ResourcePacksWatcher_StateChanged;
            resourcePacksWatcher.Renamed += ResourcePacksWatcher_StateRenamed;
            resourcePacksWatcher.EnableRaisingEvents = true;
            logger.LogInformation(
                "Local resource pack watcher started. InstanceId={InstanceId} ResourcePacksDirectory={ResourcePacksDirectory}",
                instance.Id,
                watchedResourcePacksDirectory);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            logger.LogWarning(
                exception,
                "Failed to start local resource pack watcher. InstanceId={InstanceId} ResourcePacksDirectory={ResourcePacksDirectory}",
                instance.Id,
                watchedResourcePacksDirectory);
        }
    }

    private void ResourcePacksWatcher_StateChanged(object sender, FileSystemEventArgs e)
    {
        if (!IsTrackedResourcePackPath(e.FullPath))
            return;

        QueueWatcherRefresh(e.ChangeType.ToString(), e.FullPath);
    }

    private void ResourcePacksWatcher_StateRenamed(object sender, RenamedEventArgs e)
    {
        if (!IsTrackedResourcePackPath(e.FullPath) && !IsTrackedResourcePackPath(e.OldFullPath))
            return;

        QueueWatcherRefresh("Renamed", e.FullPath);
    }

    private void QueueWatcherRefresh(string changeType, string fullPath)
    {
        var instance = selectedInstance;
        if (instance is null)
            return;

        var previousCts = Interlocked.Exchange(
            ref watcherRefreshCancellationTokenSource,
            new CancellationTokenSource());
        previousCts?.Cancel();
        previousCts?.Dispose();

        var refreshCts = watcherRefreshCancellationTokenSource!;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(200, refreshCts.Token);
                logger.LogInformation(
                    "Detected local resource pack folder change. InstanceId={InstanceId} ChangeType={ChangeType} Path={Path}",
                    instance.Id,
                    changeType,
                    fullPath);
                await RefreshResourcePacksAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Failed to refresh local resource packs after watcher update. InstanceId={InstanceId}",
                    instance.Id);
                uiDispatcher.Post(() => ReportStatus(Strings.Status_LoadLocalResourcePacksFailed));
            }
        });
    }

    private bool IsTrackedResourcePackPath(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrWhiteSpace(watchedResourcePacksDirectory))
            return false;

        var normalizedRoot = Path.GetFullPath(watchedResourcePacksDirectory);
        var normalizedPath = Path.GetFullPath(fullPath);
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
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
