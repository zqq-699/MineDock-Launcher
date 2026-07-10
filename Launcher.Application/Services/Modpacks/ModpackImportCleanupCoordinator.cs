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

using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.Application.Services;

internal sealed class ModpackImportCleanupCoordinator
{
    private readonly IGameInstanceService instanceService;
    private readonly IModpackPackageService packageService;
    private readonly IModpackInstanceStagingService stagingService;
    private readonly ILogger logger;

    public ModpackImportCleanupCoordinator(
        IGameInstanceService instanceService,
        IModpackPackageService packageService,
        IModpackInstanceStagingService stagingService,
        ILogger logger)
    {
        this.instanceService = instanceService;
        this.packageService = packageService;
        this.stagingService = stagingService;
        this.logger = logger;
    }

    public async Task CleanupFailedImportAsync(ModpackImportSession session)
    {
        if (session.StagedInstance is not null
            || session.ImportedInstance is not null
            || session.PreparedModpack is not null)
        {
            session.Progress?.Report(new LauncherProgress(ImportProgressStages.CleaningUp, string.Empty));
        }

        if (session.ImportedInstance is not null)
        {
            try
            {
                await instanceService.DeleteInstanceAsync(session.ImportedInstance.Id, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to delete partially imported instance. InstanceId={InstanceId}",
                    session.ImportedInstance.Id);
            }
        }

        if (session.StagedInstance is not null)
        {
            try
            {
                await stagingService.CleanupFailedImportAsync(
                        session.StagedInstance,
                        session.FinalVersionName,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to clean up staged modpack instance. InstanceName={InstanceName}",
                    session.StagedInstance.ResolvedInstanceName);
            }
        }

        if (session.PreparedModpack is null)
            return;

        try
        {
            await packageService.CleanupAsync(session.PreparedModpack, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to clean up prepared modpack workspace. WorkingDirectory={WorkingDirectory}",
                session.PreparedModpack.WorkingDirectory);
        }
    }

    public async Task CleanupSuccessfulImportAsync(PreparedModpack preparedModpack)
    {
        try
        {
            await packageService.CleanupAsync(preparedModpack, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to clean up modpack workspace after a successful import. WorkingDirectory={WorkingDirectory}",
                preparedModpack.WorkingDirectory);
        }
    }
}
