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

public sealed class GameInstallCoordinator : IGameInstallCoordinator
{
    private readonly SemaphoreSlim installExecutionLock = new(1, 1);
    private readonly HashSet<string> installingVersions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object installingVersionsLock = new();

    public async ValueTask<IAsyncDisposable> AcquireInstallAsync(
        string minecraftDirectory,
        string versionName,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(minecraftDirectory))
            throw new InvalidOperationException("Minecraft directory is required.");

        if (string.IsNullOrWhiteSpace(versionName))
            throw new InvalidOperationException("Version name is required.");

        var key = CreateInstallingVersionKey(minecraftDirectory, versionName);
        var lockAcquiredImmediately = installExecutionLock.Wait(0, cancellationToken);
        if (!lockAcquiredImmediately)
        {
            progress?.Report(new LauncherProgress(InstallProgressStages.Queue, string.Empty));
            await installExecutionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        lock (installingVersionsLock)
            installingVersions.Add(key);

        return new InstallLease(this, key);
    }

    public bool IsInstallingVersion(string minecraftDirectory, string versionName)
    {
        if (string.IsNullOrWhiteSpace(minecraftDirectory) || string.IsNullOrWhiteSpace(versionName))
            return false;

        lock (installingVersionsLock)
            return installingVersions.Contains(CreateInstallingVersionKey(minecraftDirectory, versionName));
    }

    private void Release(string key)
    {
        lock (installingVersionsLock)
            installingVersions.Remove(key);

        installExecutionLock.Release();
    }

    private static string CreateInstallingVersionKey(string minecraftDirectory, string versionName)
    {
        return Path.GetFullPath(Path.Combine(minecraftDirectory, "versions", versionName))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
    }

    private sealed class InstallLease(GameInstallCoordinator owner, string key) : IAsyncDisposable
    {
        private GameInstallCoordinator? owner = owner;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref owner, null)?.Release(key);
            return ValueTask.CompletedTask;
        }
    }
}
