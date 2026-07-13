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

using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels.GameSettings;

internal sealed class InstanceSettingsPersistenceCoordinator : IDisposable
{
    private readonly IGameInstanceService instanceService;
    private readonly IStatusService statusService;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger logger;
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private readonly Dictionary<string, CancellationTokenSource> pendingSaves = new(StringComparer.Ordinal);
    private readonly object pendingSavesLock = new();
    private CancellationTokenSource instanceLifetime = new();
    private GameInstance? selectedInstance;
    private string? selectedInstanceId;
    private int generation;
    private bool disposed;

    public InstanceSettingsPersistenceCoordinator(
        IGameInstanceService instanceService,
        IStatusService statusService,
        IUiDispatcher uiDispatcher,
        ILogger logger)
    {
        this.instanceService = instanceService;
        this.statusService = statusService;
        this.uiDispatcher = uiDispatcher;
        this.logger = logger;
    }

    public event Action<GameInstance>? InstanceSaved;

    public void SetInstance(GameInstance? instance)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (ReferenceEquals(selectedInstance, instance))
            return;

        selectedInstance = instance;
        selectedInstanceId = instance?.Id;
        Interlocked.Increment(ref generation);
        CancelPendingSaves();
        var nextLifetime = new CancellationTokenSource();
        var previousLifetime = Interlocked.Exchange(ref instanceLifetime, nextLifetime);
        previousLifetime.Cancel();
        previousLifetime.Dispose();
    }

    public void Schedule(
        string area,
        GameInstance instance,
        Func<GameInstance, Action?> applyMutation,
        Action restoreEditor,
        TimeSpan? delay = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!string.Equals(selectedInstanceId, instance.Id, StringComparison.OrdinalIgnoreCase))
            return;

        var requestGeneration = generation;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(instanceLifetime.Token);
        CancellationTokenSource? previous;
        lock (pendingSavesLock)
        {
            pendingSaves.Remove(area, out previous);
            pendingSaves[area] = cancellation;
        }

        previous?.Cancel();
        _ = PersistAsync(area, instance, requestGeneration, applyMutation, restoreEditor, delay, cancellation);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        CancelPendingSaves();
        instanceLifetime.Cancel();
        instanceLifetime.Dispose();
    }

    private async Task PersistAsync(
        string area,
        GameInstance instance,
        int requestGeneration,
        Func<GameInstance, Action?> applyMutation,
        Action restoreEditor,
        TimeSpan? delay,
        CancellationTokenSource cancellation)
    {
        Action? rollback = null;
        var lockTaken = false;
        try
        {
            if (delay is { } saveDelay && saveDelay > TimeSpan.Zero)
                await Task.Delay(saveDelay, cancellation.Token);

            await saveGate.WaitAsync(cancellation.Token);
            lockTaken = true;
            if (!IsCurrent(instance, requestGeneration))
                return;

            rollback = applyMutation(instance);
            if (rollback is null)
                return;

            await instanceService.SaveInstanceAsync(instance, cancellation.Token);
            if (IsCurrent(instance, requestGeneration))
                uiDispatcher.Post(() => InstanceSaved?.Invoke(instance));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            rollback?.Invoke();
        }
        catch (Exception exception)
        {
            rollback?.Invoke();
            logger.LogError(
                exception,
                "Failed to save instance settings. InstanceId={InstanceId} Area={Area}",
                instance.Id,
                area);
            if (IsCurrent(instance, requestGeneration))
            {
                uiDispatcher.Post(() =>
                {
                    restoreEditor();
                    statusService.Report(Strings.Status_InstanceSettingsSaveFailed);
                });
            }
        }
        finally
        {
            if (lockTaken)
                saveGate.Release();

            lock (pendingSavesLock)
            {
                if (pendingSaves.TryGetValue(area, out var current) && ReferenceEquals(current, cancellation))
                    pendingSaves.Remove(area);
            }

            cancellation.Dispose();
        }
    }

    private bool IsCurrent(GameInstance instance, int requestGeneration)
    {
        return requestGeneration == generation
            && string.Equals(selectedInstanceId, instance.Id, StringComparison.OrdinalIgnoreCase);
    }

    private void CancelPendingSaves()
    {
        CancellationTokenSource[] pending;
        lock (pendingSavesLock)
        {
            pending = pendingSaves.Values.ToArray();
            pendingSaves.Clear();
        }

        foreach (var cancellation in pending)
            cancellation.Cancel();
    }
}
