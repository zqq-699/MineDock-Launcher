/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Application.Services;

public interface IInstanceDeletionCleanupService
{
    Task CleanupPendingAsync(CancellationToken cancellationToken = default);
}
