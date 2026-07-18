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

using System.IO;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.FileSystem;

public sealed class InstanceDirectoryMonitor(
    ILogger<InstanceDirectoryMonitor>? logger = null) : IInstanceDirectoryMonitor
{
    private readonly ILogger<InstanceDirectoryMonitor> logger = logger ?? NullLogger<InstanceDirectoryMonitor>.Instance;

    public IInstanceDirectoryWatch Watch(GameInstance instance, InstanceDirectoryKind directoryKind)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (string.IsNullOrWhiteSpace(instance.InstanceDirectory))
            return EmptyInstanceDirectoryWatch.Instance;

        var instanceDirectory = Path.GetFullPath(instance.InstanceDirectory);
        if (!Directory.Exists(instanceDirectory))
        {
            logger.LogDebug(
                "Instance directory watcher was not started because the instance root does not exist. InstanceId={InstanceId} DirectoryKind={DirectoryKind}",
                instance.Id,
                directoryKind);
            return EmptyInstanceDirectoryWatch.Instance;
        }

        try
        {
            return new InstanceDirectoryWatch(
                instanceDirectory,
                ResolveDirectoryName(directoryKind),
                directoryKind,
                instance.Id,
                logger,
                includeTargetSubdirectories: directoryKind is InstanceDirectoryKind.Saves);
        }
        catch (Exception exception) when (
            exception is DirectoryNotFoundException
            || exception is IOException && !Directory.Exists(instanceDirectory))
        {
            logger.LogDebug(
                exception,
                "Instance directory disappeared while starting its watcher. InstanceId={InstanceId} DirectoryKind={DirectoryKind}",
                instance.Id,
                directoryKind);
            return EmptyInstanceDirectoryWatch.Instance;
        }
    }

    private static string ResolveDirectoryName(InstanceDirectoryKind directoryKind)
    {
        return directoryKind switch
        {
            InstanceDirectoryKind.Mods => "mods",
            InstanceDirectoryKind.Saves => "saves",
            InstanceDirectoryKind.ResourcePacks => "resourcepacks",
            InstanceDirectoryKind.ShaderPacks => "shaderpacks",
            _ => throw new ArgumentOutOfRangeException(nameof(directoryKind), directoryKind, null)
        };
    }

    private sealed class InstanceDirectoryWatch : IInstanceDirectoryWatch
    {
        private readonly FileSystemWatcher watcher;
        private readonly ILogger logger;
        private readonly string instanceDirectory;
        private readonly string targetDirectoryName;
        private readonly string instanceId;
        private readonly InstanceDirectoryKind directoryKind;
        private readonly bool includeTargetSubdirectories;
        private bool disposed;

        public InstanceDirectoryWatch(
            string instanceDirectory,
            string targetDirectoryName,
            InstanceDirectoryKind directoryKind,
            string instanceId,
            ILogger logger,
            bool includeTargetSubdirectories)
        {
            this.instanceDirectory = instanceDirectory;
            this.targetDirectoryName = targetDirectoryName;
            this.directoryKind = directoryKind;
            this.instanceId = instanceId;
            this.logger = logger;
            this.includeTargetSubdirectories = includeTargetSubdirectories;
            watcher = new FileSystemWatcher(instanceDirectory, "*")
            {
                // Watching the existing root lets us observe a content directory that is created later
                // without creating that directory as a side effect of starting the watcher.
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.DirectoryName
                    | NotifyFilters.FileName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size
                    | NotifyFilters.CreationTime
            };
            watcher.Changed += Watcher_Changed;
            watcher.Created += Watcher_Changed;
            watcher.Deleted += Watcher_Changed;
            watcher.Renamed += Watcher_Renamed;
            watcher.Error += Watcher_Error;
            watcher.EnableRaisingEvents = true;
            logger.LogDebug(
                "Instance directory watcher started. InstanceId={InstanceId} DirectoryKind={DirectoryKind}",
                instanceId,
                directoryKind);
        }

        public event EventHandler<InstanceDirectoryChangedEventArgs>? Changed;

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= Watcher_Changed;
            watcher.Created -= Watcher_Changed;
            watcher.Deleted -= Watcher_Changed;
            watcher.Renamed -= Watcher_Renamed;
            watcher.Error -= Watcher_Error;
            watcher.Dispose();
        }

        private void Watcher_Changed(object sender, FileSystemEventArgs e)
        {
            if (!IsInTargetScope(e.FullPath))
                return;
            Changed?.Invoke(this, new InstanceDirectoryChangedEventArgs(e.ChangeType.ToString(), e.FullPath));
        }

        private void Watcher_Renamed(object sender, RenamedEventArgs e)
        {
            if (!IsInTargetScope(e.FullPath) && !IsInTargetScope(e.OldFullPath))
                return;
            Changed?.Invoke(this, new InstanceDirectoryChangedEventArgs("Renamed", e.FullPath, e.OldFullPath));
        }

        private bool IsInTargetScope(string fullPath)
        {
            var relativePath = Path.GetRelativePath(instanceDirectory, fullPath);
            if (Path.IsPathRooted(relativePath)
                || relativePath.Equals("..", StringComparison.Ordinal)
                || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                return false;
            }

            if (relativePath.Equals(targetDirectoryName, StringComparison.OrdinalIgnoreCase))
                return true;

            var parent = Path.GetDirectoryName(relativePath);
            if (!includeTargetSubdirectories)
                return parent?.Equals(targetDirectoryName, StringComparison.OrdinalIgnoreCase) is true;

            return relativePath.StartsWith(
                targetDirectoryName + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }

        private void Watcher_Error(object sender, ErrorEventArgs e)
        {
            logger.LogWarning(
                e.GetException(),
                "Instance directory watcher reported an error. InstanceId={InstanceId} DirectoryKind={DirectoryKind}",
                instanceId,
                directoryKind);
        }
    }

    private sealed class EmptyInstanceDirectoryWatch : IInstanceDirectoryWatch
    {
        public static EmptyInstanceDirectoryWatch Instance { get; } = new();

        public event EventHandler<InstanceDirectoryChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public void Dispose()
        {
        }
    }
}
