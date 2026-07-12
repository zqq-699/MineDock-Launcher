/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Application.Services;

public sealed class InstanceRenameRecoveryService(
    ISettingsService settingsService,
    IGameInstanceRepository repository,
    ILogger<InstanceRenameRecoveryService>? logger = null) : IInstanceRenameRecoveryService
{
    private readonly ILogger<InstanceRenameRecoveryService> logger =
        logger ?? NullLogger<InstanceRenameRecoveryService>.Instance;

    public async Task RecoverPendingAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await repository.RecoverPendingVersionRenamesAsync(settings.MinecraftDirectory, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            this.logger.LogWarning(
                exception,
                "Pending instance rename recovery did not complete. MinecraftDirectory={MinecraftDirectory}",
                settings.MinecraftDirectory);
        }
    }
}
