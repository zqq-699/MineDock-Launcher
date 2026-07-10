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

namespace Launcher.Application.Services;

public interface IModpackInstanceStagingService
{
    Task<StagedModpackInstance> StageAsync(
        PreparedModpack preparedModpack,
        string preferredInstanceName,
        CancellationToken cancellationToken = default);

    Task<GameInstance> FinalizeAsync(
        StagedModpackInstance stagedInstance,
        string finalVersionName,
        CancellationToken cancellationToken = default);

    Task CleanupFailedImportAsync(
        StagedModpackInstance stagedInstance,
        string? finalVersionName,
        CancellationToken cancellationToken = default);
}

public sealed class StagedModpackInstance
{
    public string ResolvedInstanceName { get; init; } = string.Empty;

    public string MinecraftDirectory { get; init; } = string.Empty;

    public string InstanceDirectory { get; init; } = string.Empty;

    public GameInstance Instance { get; init; } = new();
}
