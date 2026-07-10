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

public sealed class LocalShaderPacksViewModel : IDisposable
{
    private readonly ILocalShaderPackService localShaderPackService;
    private readonly IStatusService statusService;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger<LocalShaderPacksViewModel> logger;
    private CancellationTokenSource? refreshCancellationTokenSource;
    private FileSystemWatcher? shaderPacksWatcher;
    private CancellationTokenSource? watcherRefreshCancellationTokenSource;
    private GameInstance? selectedInstance;
    private IReadOnlyList<LocalShaderPack> currentShaderPacks = Array.Empty<LocalShaderPack>();
    private string? watchedShaderPacksDirectory;
    private bool watcherEnabled;
    private bool watcherSuspendedForRename;
    private int shaderPackRefreshVersion;

    public LocalShaderPacksViewModel(
        ILocalShaderPackService localShaderPackService,
        IStatusService statusService,
        IUiDispatcher? uiDispatcher = null,
        ILogger<LocalShaderPacksViewModel>? logger = null)
    {
        this.localShaderPackService = localShaderPackService;
        this.statusService = statusService;
        this.uiDispatcher = uiDispatcher ?? ImmediateUiDispatcher.Instance;
        this.logger = logger ?? NullLogger<LocalShaderPacksViewModel>.Instance;
    }

    public event EventHandler? ShaderPacksChanged;

    public ObservableCollection<LocalShaderPack> ShaderPacks { get; } = [];

    public IReadOnlyList<LocalShaderPack> CurrentShaderPacks => currentShaderPacks;

    public void SetSelectedInstance(GameInstance? instance)
    {
        selectedInstance = instance;
        Interlocked.Increment(ref shaderPackRefreshVersion);
        CancelRefresh();
        ResetWatcher();
        ClearShaderPacks();
        logger.LogInformation(
            "Selected instance changed for local shader packs view. InstanceId={InstanceId}",
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
        return RefreshShaderPacksAsync();
    }

    public async Task RefreshShaderPacksAsync()
    {
        var refreshVersion = Interlocked.Increment(ref shaderPackRefreshVersion);
        var refreshCts = ReplaceRefreshCancellationTokenSource();
        var instance = selectedInstance;

        if (instance is null)
        {
            ClearShaderPacks();
            logger.LogInformation("Local shader packs view cleared because no instance is selected.");
            return;
        }

        IReadOnlyList<LocalShaderPack> loadedShaderPacks;
        try
        {
            loadedShaderPacks = await localShaderPackService.GetShaderPacksAsync(instance, refreshCts.Token);
        }
        catch (OperationCanceledException) when (refreshCts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to load local shader packs. InstanceId={InstanceId}",
                instance.Id);
            throw;
        }
        finally
        {
            ReleaseRefreshCancellationTokenSource(refreshCts);
        }

        if (refreshVersion != shaderPackRefreshVersion
            || !string.Equals(instance.Id, selectedInstance?.Id, StringComparison.Ordinal))
        {
            return;
        }

        uiDispatcher.Invoke(() =>
        {
            currentShaderPacks = loadedShaderPacks;
            ShaderPacks.ReplaceWith(loadedShaderPacks);
            ShaderPacksChanged?.Invoke(this, EventArgs.Empty);
        });
        logger.LogInformation(
            "Local shader packs view refreshed. InstanceId={InstanceId} Count={ShaderPackCount}",
            instance.Id,
            ShaderPacks.Count);
    }

    public async Task<int> DeleteShaderPacksAsync(IEnumerable<LocalShaderPack> shaderPacks)
    {
        ArgumentNullException.ThrowIfNull(shaderPacks);

        var failedCount = 0;
        foreach (var shaderPack in shaderPacks.DistinctBy(shaderPack => shaderPack.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                await localShaderPackService.DeleteAsync(shaderPack);
            }
            catch (Exception exception)
            {
                failedCount++;
                logger.LogWarning(
                    exception,
                    "Failed to delete local shader pack. Path={Path}",
                    shaderPack.FullPath);
            }
        }

        await RefreshShaderPacksAsync();
        return failedCount;
    }

    public async Task<LocalShaderPackImportResult> ImportShaderPackAsync(string archivePath, bool reportStatus = true)
    {
        if (selectedInstance is null || string.IsNullOrWhiteSpace(archivePath))
            return LocalShaderPackImportResult.Failure(LocalShaderPackImportFailureReason.UnexpectedError);

        var result = await localShaderPackService.ImportAsync(selectedInstance, archivePath);
        if (!result.IsSuccess)
        {
            switch (result.FailureReason)
            {
                case LocalShaderPackImportFailureReason.FileNotFound:
                    if (reportStatus)
                        ReportStatus(Strings.Status_LocalShaderPackImportFileNotFound);
                    break;
                case LocalShaderPackImportFailureReason.UnexpectedError:
                    if (reportStatus)
                        ReportStatus(Strings.Status_LocalShaderPackImportFailed);
                    break;
            }

            return result;
        }

        await RefreshShaderPacksAsync();
        if (reportStatus)
            ReportStatus(Strings.Status_LocalShaderPackImported);
        return result;
    }

    public void Dispose()
    {
        shaderPacksWatcher?.Dispose();
        shaderPacksWatcher = null;
        CancelRefresh();
        watcherRefreshCancellationTokenSource?.Cancel();
        watcherRefreshCancellationTokenSource?.Dispose();
        watcherRefreshCancellationTokenSource = null;
    }

    private void ResetWatcher()
    {
        shaderPacksWatcher?.Dispose();
        shaderPacksWatcher = null;
        watcherRefreshCancellationTokenSource?.Cancel();
        watcherRefreshCancellationTokenSource?.Dispose();
        watcherRefreshCancellationTokenSource = null;
        watchedShaderPacksDirectory = null;

        if (!watcherEnabled || watcherSuspendedForRename)
            return;

        var instance = selectedInstance;
        if (instance is null || string.IsNullOrWhiteSpace(instance.InstanceDirectory))
            return;

        watchedShaderPacksDirectory = Path.Combine(instance.InstanceDirectory, "shaderpacks");
        Directory.CreateDirectory(watchedShaderPacksDirectory);

        try
        {
            shaderPacksWatcher = new FileSystemWatcher(watchedShaderPacksDirectory, "*")
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.DirectoryName
                    | NotifyFilters.FileName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size
                    | NotifyFilters.CreationTime
            };
            shaderPacksWatcher.Changed += ShaderPacksWatcher_StateChanged;
            shaderPacksWatcher.Created += ShaderPacksWatcher_StateChanged;
            shaderPacksWatcher.Deleted += ShaderPacksWatcher_StateChanged;
            shaderPacksWatcher.Renamed += ShaderPacksWatcher_StateRenamed;
            shaderPacksWatcher.EnableRaisingEvents = true;
            logger.LogInformation(
                "Local shader pack watcher started. InstanceId={InstanceId} ShaderPacksDirectory={ShaderPacksDirectory}",
                instance.Id,
                watchedShaderPacksDirectory);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            logger.LogWarning(
                exception,
                "Failed to start local shader pack watcher. InstanceId={InstanceId} ShaderPacksDirectory={ShaderPacksDirectory}",
                instance.Id,
                watchedShaderPacksDirectory);
        }
    }

    private void ShaderPacksWatcher_StateChanged(object sender, FileSystemEventArgs e)
    {
        if (!IsTrackedShaderPackPath(e.FullPath))
            return;

        QueueWatcherRefresh(e.ChangeType.ToString(), e.FullPath);
    }

    private void ShaderPacksWatcher_StateRenamed(object sender, RenamedEventArgs e)
    {
        if (!IsTrackedShaderPackPath(e.FullPath) && !IsTrackedShaderPackPath(e.OldFullPath))
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
                    "Detected local shader pack folder change. InstanceId={InstanceId} ChangeType={ChangeType} Path={Path}",
                    instance.Id,
                    changeType,
                    fullPath);
                await RefreshShaderPacksAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Failed to refresh local shader packs after watcher update. InstanceId={InstanceId}",
                    instance.Id);
                uiDispatcher.Post(() => ReportStatus(Strings.Status_LoadLocalShaderPacksFailed));
            }
        });
    }

    private bool IsTrackedShaderPackPath(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrWhiteSpace(watchedShaderPacksDirectory))
            return false;

        var normalizedRoot = Path.GetFullPath(watchedShaderPacksDirectory);
        var normalizedPath = Path.GetFullPath(fullPath);
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private void ReportStatus(string message)
    {
        statusService.Report(message);
    }

    private void ClearShaderPacks()
    {
        uiDispatcher.Invoke(() =>
        {
            currentShaderPacks = Array.Empty<LocalShaderPack>();
            ShaderPacks.Clear();
            ShaderPacksChanged?.Invoke(this, EventArgs.Empty);
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
