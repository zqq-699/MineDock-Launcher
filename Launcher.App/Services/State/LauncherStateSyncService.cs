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

using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.Services;

public sealed class LauncherStateSyncService : IDisposable
{
    private static readonly TimeSpan DefaultDebounceDelay = TimeSpan.FromMilliseconds(300);
    private readonly object syncLock = new();
    private readonly ILauncherStateMonitor stateMonitor;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger<LauncherStateSyncService> logger;
    private readonly TimeSpan debounceDelay;
    private Func<LauncherSettings>? settingsProvider;
    private Func<Task>? synchronize;
    private CancellationTokenSource? pendingCancellation;
    private Task pendingTask = Task.CompletedTask;
    private bool isStarted;
    private bool isDisposed;

    public LauncherStateSyncService(
        ILauncherStateMonitor stateMonitor,
        IUiDispatcher uiDispatcher,
        ILogger<LauncherStateSyncService>? logger = null,
        TimeSpan? debounceDelay = null)
    {
        this.stateMonitor = stateMonitor;
        this.uiDispatcher = uiDispatcher;
        this.logger = logger ?? NullLogger<LauncherStateSyncService>.Instance;
        this.debounceDelay = debounceDelay ?? DefaultDebounceDelay;
    }

    public void Start(Func<LauncherSettings> settingsProvider, Func<Task> synchronize)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(settingsProvider);
        ArgumentNullException.ThrowIfNull(synchronize);

        if (isStarted)
            Stop();
        this.settingsProvider = settingsProvider;
        this.synchronize = synchronize;
        stateMonitor.StateChanged += StateMonitor_StateChanged;
        stateMonitor.Watch(settingsProvider());
        isStarted = true;
    }

    public void RequestSync()
    {
        if (!isStarted)
            return;

        var cancellation = new CancellationTokenSource();
        lock (syncLock)
        {
            pendingCancellation?.Cancel();
            pendingCancellation = cancellation;
            pendingTask = SynchronizeAfterDelayAsync(cancellation);
        }
    }

    public Task WaitForPendingSyncAsync()
    {
        lock (syncLock)
            return pendingTask;
    }

    public void Stop()
    {
        if (!isStarted)
            return;

        stateMonitor.StateChanged -= StateMonitor_StateChanged;

        isStarted = false;
        settingsProvider = null;
        synchronize = null;
        lock (syncLock)
        {
            pendingCancellation?.Cancel();
            pendingCancellation = null;
        }

        stateMonitor.Stop();
    }

    public void Dispose()
    {
        if (isDisposed)
            return;

        Stop();
        isDisposed = true;
    }

    private void StateMonitor_StateChanged(object? sender, EventArgs e)
    {
        RequestSync();
    }

    private async Task SynchronizeAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(debounceDelay, cancellation.Token).ConfigureAwait(false);
            var synchronizeCurrentState = synchronize;
            var getSettings = settingsProvider;
            if (synchronizeCurrentState is null || getSettings is null)
                return;

            await uiDispatcher.InvokeAsync(synchronizeCurrentState).ConfigureAwait(false);
            if (!cancellation.IsCancellationRequested && isStarted)
                stateMonitor.Watch(getSettings());
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to synchronize launcher state after a monitored change.");
        }
        finally
        {
            lock (syncLock)
            {
                if (ReferenceEquals(pendingCancellation, cancellation))
                    pendingCancellation = null;
            }

            cancellation.Dispose();
        }
    }
}
