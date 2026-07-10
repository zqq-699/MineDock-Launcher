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

public sealed class LocalSavesViewModel : IDisposable
{
    private readonly ILocalSaveService localSaveService;
    private readonly IStatusService statusService;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger<LocalSavesViewModel> logger;
    private readonly InstanceContentRefreshWatcher contentWatcher;
    private CancellationTokenSource? refreshCancellationTokenSource;
    private GameInstance? selectedInstance;
    private IReadOnlyList<LocalSave> currentSaves = Array.Empty<LocalSave>();
    private int saveRefreshVersion;

    public LocalSavesViewModel(
        ILocalSaveService localSaveService,
        IStatusService statusService,
        IInstanceDirectoryMonitor instanceDirectoryMonitor,
        IUiDispatcher? uiDispatcher = null,
        ILogger<LocalSavesViewModel>? logger = null)
    {
        this.localSaveService = localSaveService;
        this.statusService = statusService;
        this.uiDispatcher = uiDispatcher ?? ImmediateUiDispatcher.Instance;
        this.logger = logger ?? NullLogger<LocalSavesViewModel>.Instance;
        contentWatcher = new InstanceContentRefreshWatcher(
            instanceDirectoryMonitor,
            InstanceDirectoryKind.Saves,
            RefreshSavesAsync,
            _ => this.uiDispatcher.Post(() => ReportStatus(Strings.Status_LoadLocalSavesFailed)),
            this.logger);
    }

    public event EventHandler? SavesChanged;

    public ObservableCollection<LocalSave> Saves { get; } = [];

    public IReadOnlyList<LocalSave> CurrentSaves => currentSaves;

    public void SetSelectedInstance(GameInstance? instance)
    {
        selectedInstance = instance;
        Interlocked.Increment(ref saveRefreshVersion);
        CancelRefresh();
        contentWatcher.SetInstance(instance);
        ClearSaves();
        logger.LogInformation(
            "Selected instance changed for local saves view. InstanceId={InstanceId}",
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
        return RefreshSavesAsync();
    }

    public async Task RefreshSavesAsync()
    {
        var refreshVersion = Interlocked.Increment(ref saveRefreshVersion);
        var refreshCts = ReplaceRefreshCancellationTokenSource();
        var instance = selectedInstance;

        if (instance is null)
        {
            ClearSaves();
            logger.LogInformation("Local saves view cleared because no instance is selected.");
            return;
        }

        IReadOnlyList<LocalSave> loadedSaves;
        try
        {
            loadedSaves = await localSaveService.GetSavesAsync(instance, refreshCts.Token);
        }
        catch (OperationCanceledException) when (refreshCts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to load local saves. InstanceId={InstanceId}",
                instance.Id);
            throw;
        }
        finally
        {
            ReleaseRefreshCancellationTokenSource(refreshCts);
        }

        if (refreshVersion != saveRefreshVersion
            || !string.Equals(instance.Id, selectedInstance?.Id, StringComparison.Ordinal))
        {
            return;
        }

        uiDispatcher.Invoke(() =>
        {
            currentSaves = loadedSaves;
            Saves.ReplaceWith(loadedSaves);
            SavesChanged?.Invoke(this, EventArgs.Empty);
        });
        logger.LogInformation(
            "Local saves view refreshed. InstanceId={InstanceId} Count={SaveCount}",
            instance.Id,
            Saves.Count);
    }

    public async Task DeleteSaveAsync(LocalSave save)
    {
        await localSaveService.DeleteAsync(save);
        await RefreshSavesAsync();
    }

    public async Task<int> DeleteSavesAsync(IEnumerable<LocalSave> saves)
    {
        ArgumentNullException.ThrowIfNull(saves);

        var failedCount = 0;
        foreach (var save in saves.DistinctBy(save => save.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                await localSaveService.DeleteAsync(save);
            }
            catch (Exception exception)
            {
                failedCount++;
                logger.LogWarning(
                    exception,
                    "Failed to delete local save. Path={Path}",
                    save.FullPath);
            }
        }

        await RefreshSavesAsync();
        return failedCount;
    }

    public async Task<LocalSaveImportResult> ImportSaveFromArchiveAsync(string archivePath, bool reportStatus = true)
    {
        if (selectedInstance is null || string.IsNullOrWhiteSpace(archivePath))
            return LocalSaveImportResult.Failure(LocalSaveImportFailureReason.UnexpectedError);

        var result = await localSaveService.ImportFromArchiveAsync(selectedInstance, archivePath);
        if (!result.IsSuccess)
        {
            switch (result.FailureReason)
            {
                case LocalSaveImportFailureReason.FileNotFound:
                    if (reportStatus)
                        ReportStatus(Strings.Status_LocalSaveImportFileNotFound);
                    break;
                case LocalSaveImportFailureReason.UnexpectedError:
                    if (reportStatus)
                        ReportStatus(Strings.Status_LocalSaveImportFailed);
                    break;
            }

            return result;
        }

        await RefreshSavesAsync();
        if (reportStatus)
            ReportStatus(Strings.Status_LocalSaveImported);
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

    private void ClearSaves()
    {
        uiDispatcher.Invoke(() =>
        {
            currentSaves = Array.Empty<LocalSave>();
            Saves.Clear();
            SavesChanged?.Invoke(this, EventArgs.Empty);
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
