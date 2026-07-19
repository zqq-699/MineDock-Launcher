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

using Launcher.App.ViewModels.Download;
using Launcher.App.ViewModels.Settings;
using Launcher.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.Services;

public sealed class LauncherShutdownService
{
    private readonly object shutdownLock = new();
    private readonly DownloadTasksPageViewModel downloadTasksPage;
    private readonly SettingsPageViewModel? settingsPage;
    private readonly IInstanceInstallCleanupService installCleanupService;
    private readonly IModpackWorkspaceCleanupService workspaceCleanupService;
    private readonly IModpackSandboxCleanupService sandboxCleanupService;
    private readonly IMultiplayerLobbyService? multiplayerLobbyService;
    private readonly ILogger<LauncherShutdownService> logger;
    private Task? shutdownTask;

    public LauncherShutdownService(
        DownloadTasksPageViewModel downloadTasksPage,
        IInstanceInstallCleanupService installCleanupService,
        IModpackWorkspaceCleanupService workspaceCleanupService,
        IModpackSandboxCleanupService sandboxCleanupService,
        SettingsPageViewModel? settingsPage = null,
        ILogger<LauncherShutdownService>? logger = null,
        IMultiplayerLobbyService? multiplayerLobbyService = null)
    {
        this.downloadTasksPage = downloadTasksPage;
        this.settingsPage = settingsPage;
        this.installCleanupService = installCleanupService;
        this.workspaceCleanupService = workspaceCleanupService;
        this.sandboxCleanupService = sandboxCleanupService;
        this.multiplayerLobbyService = multiplayerLobbyService;
        this.logger = logger ?? NullLogger<LauncherShutdownService>.Instance;
    }

    public Task PrepareForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        lock (shutdownLock)
            return shutdownTask ??= PrepareForExitCoreAsync(timeout, cancellationToken);
    }

    private async Task PrepareForExitCoreAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout);

        downloadTasksPage.CancelAllRunningTasks();
        try
        {
            if (multiplayerLobbyService is not null)
                await multiplayerLobbyService.StopAsync(timeoutCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
            logger.LogWarning("Timed out stopping the multiplayer lobby during launcher exit.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to stop the multiplayer lobby during launcher exit.");
        }

        try
        {
            if (settingsPage is not null)
                await settingsPage.FlushPendingSettingsAsync(timeoutCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
            logger.LogWarning("Timed out flushing pending launcher settings during exit.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to flush pending launcher settings during exit.");
        }

        try
        {
            var completed = await downloadTasksPage
                .WaitForTrackedBackgroundTasksAsync(timeout, timeoutCancellation.Token)
                .ConfigureAwait(false);
            if (!completed)
                logger.LogWarning("Timed out waiting for background download tasks during launcher exit.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed while waiting for background download tasks during launcher exit.");
        }

        try
        {
            await sandboxCleanupService
                .WaitForPendingCleanupAsync(timeoutCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
            logger.LogWarning("Timed out cleaning modpack loader sandboxes during launcher exit.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed while cleaning modpack loader sandboxes during launcher exit.");
        }

        try
        {
            await installCleanupService
                .CleanupPendingAsync(timeoutCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
            logger.LogWarning("Timed out cleaning pending instance installations during launcher exit.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to clean pending instance installations during launcher exit.");
        }

        try
        {
            await workspaceCleanupService
                .CleanupAllAsync(timeoutCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
        {
            logger.LogWarning("Timed out cleaning modpack workspaces during launcher exit.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to clean modpack workspaces during launcher exit.");
        }
    }
}
