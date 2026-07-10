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
    private readonly ILocalShaderPackService localShaderPackService;
    private readonly IStatusService statusService;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger<LocalShaderPacksViewModel> logger;
    private readonly InstanceContentRefreshWatcher contentWatcher;
    private CancellationTokenSource? refreshCancellationTokenSource;
    private GameInstance? selectedInstance;
    private IReadOnlyList<LocalShaderPack> currentShaderPacks = Array.Empty<LocalShaderPack>();
    private int shaderPackRefreshVersion;

    public LocalShaderPacksViewModel(
        ILocalShaderPackService localShaderPackService,
        IStatusService statusService,
        IInstanceDirectoryMonitor instanceDirectoryMonitor,
        IUiDispatcher? uiDispatcher = null,
        ILogger<LocalShaderPacksViewModel>? logger = null)
    {
        this.localShaderPackService = localShaderPackService;
        this.statusService = statusService;
        this.uiDispatcher = uiDispatcher ?? ImmediateUiDispatcher.Instance;
        this.logger = logger ?? NullLogger<LocalShaderPacksViewModel>.Instance;
        contentWatcher = new InstanceContentRefreshWatcher(
            instanceDirectoryMonitor,
            InstanceDirectoryKind.ShaderPacks,
            RefreshShaderPacksAsync,
            _ => this.uiDispatcher.Post(() => ReportStatus(Strings.Status_LoadLocalShaderPacksFailed)),
            this.logger);
    }

    public event EventHandler? ShaderPacksChanged;

    public ObservableCollection<LocalShaderPack> ShaderPacks { get; } = [];

    public IReadOnlyList<LocalShaderPack> CurrentShaderPacks => currentShaderPacks;

    public void SetSelectedInstance(GameInstance? instance)
    {
        selectedInstance = instance;
        Interlocked.Increment(ref shaderPackRefreshVersion);
        CancelRefresh();
        contentWatcher.SetInstance(instance);
        ClearShaderPacks();
        logger.LogInformation(
            "Selected instance changed for local shader packs view. InstanceId={InstanceId}",
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
        contentWatcher.Dispose();
        CancelRefresh();
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
