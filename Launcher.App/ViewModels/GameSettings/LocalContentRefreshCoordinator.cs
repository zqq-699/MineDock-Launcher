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

using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels.GameSettings;

internal sealed class LocalContentRefreshCoordinator<TContent> : IDisposable
{
    private readonly Func<GameInstance, CancellationToken, Task<IReadOnlyList<TContent>>> loadAsync;
    private readonly InstanceDirectoryKind directoryKind;
    private readonly Action<IReadOnlyList<TContent>> apply;
    private readonly Action clear;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger logger;
    private readonly InstanceContentRefreshWatcher watcher;
    private CancellationTokenSource? refreshCancellation;
    private int refreshGeneration;

    public LocalContentRefreshCoordinator(
        IInstanceDirectoryMonitor monitor,
        InstanceDirectoryKind directoryKind,
        Func<GameInstance, CancellationToken, Task<IReadOnlyList<TContent>>> loadAsync,
        Action<IReadOnlyList<TContent>> apply,
        Action clear,
        Action<Exception> reportWatcherFailure,
        IUiDispatcher uiDispatcher,
        ILogger logger,
        Func<InstanceDirectoryChangedEventArgs, bool>? shouldRefresh = null)
    {
        this.loadAsync = loadAsync;
        this.directoryKind = directoryKind;
        this.apply = apply;
        this.clear = clear;
        this.uiDispatcher = uiDispatcher;
        this.logger = logger;
        watcher = new InstanceContentRefreshWatcher(
            monitor,
            directoryKind,
            RefreshAsync,
            reportWatcherFailure,
            logger,
            shouldRefresh);
    }

    public GameInstance? SelectedInstance { get; private set; }

    public IReadOnlyList<TContent> CurrentItems { get; private set; } = [];

    public void SetInstance(GameInstance? instance)
    {
        SelectedInstance = instance;
        Interlocked.Increment(ref refreshGeneration);
        CancelRefresh();
        watcher.SetInstance(instance);
        CurrentItems = [];
        uiDispatcher.Invoke(clear);
    }

    public void SetWatcherEnabled(bool enabled) => watcher.SetEnabled(enabled);

    public void SuspendForRename()
    {
        watcher.Suspend();
        CancelRefresh();
    }

    public void ResumeAfterRename() => watcher.Resume();

    public async Task RefreshAsync()
    {
        var generation = Interlocked.Increment(ref refreshGeneration);
        var replacement = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref refreshCancellation, replacement);
        previous?.Cancel();
        previous?.Dispose();
        var instance = SelectedInstance;

        if (instance is null)
        {
            CurrentItems = [];
            uiDispatcher.Invoke(clear);
            Release(replacement);
            return;
        }

        try
        {
            var items = await loadAsync(instance, replacement.Token).ConfigureAwait(false);
            if (generation != refreshGeneration
                || replacement.IsCancellationRequested
                || !string.Equals(instance.Id, SelectedInstance?.Id, StringComparison.Ordinal))
            {
                return;
            }

            uiDispatcher.Invoke(() =>
            {
                if (generation != refreshGeneration || replacement.IsCancellationRequested)
                    return;
                CurrentItems = items;
                apply(items);
            });
        }
        catch (OperationCanceledException) when (replacement.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to refresh local instance content. InstanceId={InstanceId} DirectoryKind={DirectoryKind}", instance.Id, directoryKind);
            throw;
        }
        finally
        {
            Release(replacement);
        }
    }

    public void Dispose()
    {
        watcher.Dispose();
        CancelRefresh();
    }

    private void CancelRefresh()
    {
        var cancellation = Interlocked.Exchange(ref refreshCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void Release(CancellationTokenSource cancellation)
    {
        if (ReferenceEquals(Interlocked.CompareExchange(ref refreshCancellation, null, cancellation), cancellation))
            cancellation.Dispose();
    }
}
