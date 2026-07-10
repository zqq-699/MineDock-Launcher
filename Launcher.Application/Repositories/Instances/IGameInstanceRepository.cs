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

namespace Launcher.Application.Repositories;

public interface IGameInstanceRepository
{
    Task<IReadOnlyList<GameInstance>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GameInstance>> GetAllAsync(string minecraftDirectory, CancellationToken cancellationToken = default);

    Task SaveAllAsync(IReadOnlyCollection<GameInstance> instances, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InstalledGameVersion>> DiscoverInstalledVersionsAsync(
        string minecraftDirectory,
        CancellationToken cancellationToken = default);

    string GetUniqueInstanceDirectory(string dataDirectory, string name);

    string GetVersionDirectory(string minecraftDirectory, string versionName);

    bool IsInstanceInstalled(GameInstance instance, string minecraftDirectory);

    void CreateInstanceDirectories(string directory);

    void DeleteVersionDirectory(string minecraftDirectory, string versionName);

    Task RenameVersionAsync(
        string minecraftDirectory,
        string oldVersionName,
        string newVersionName,
        CancellationToken cancellationToken = default);
}
