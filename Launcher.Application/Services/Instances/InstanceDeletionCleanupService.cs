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

public sealed class InstanceDeletionCleanupService : IInstanceDeletionCleanupService
{
    private readonly ISettingsService settingsService;
    private readonly IGameInstanceRepository repository;
    private readonly ILogger<InstanceDeletionCleanupService> logger;

    public InstanceDeletionCleanupService(
        ISettingsService settingsService,
        IGameInstanceRepository repository,
        ILogger<InstanceDeletionCleanupService>? logger = null)
    {
        this.settingsService = settingsService;
        this.repository = repository;
        this.logger = logger ?? NullLogger<InstanceDeletionCleanupService>.Instance;
    }

    public async Task CleanupPendingAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Pending instance deletion cleanup started. MinecraftDirectory={MinecraftDirectory}",
            settings.MinecraftDirectory);
        await repository
            .CleanupStagedVersionDirectoriesAsync(settings.MinecraftDirectory, cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation(
            "Pending instance deletion cleanup completed. MinecraftDirectory={MinecraftDirectory}",
            settings.MinecraftDirectory);
    }
}
