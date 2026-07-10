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
        var directory = Path.Combine(instance.InstanceDirectory, ResolveDirectoryName(directoryKind));
        Directory.CreateDirectory(directory);
        return new InstanceDirectoryWatch(
            directory,
            directoryKind,
            instance.Id,
            logger,
            includeSubdirectories: directoryKind is InstanceDirectoryKind.Saves);
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
        private readonly string instanceId;
        private readonly InstanceDirectoryKind directoryKind;
        private bool disposed;

        public InstanceDirectoryWatch(
            string directory,
            InstanceDirectoryKind directoryKind,
            string instanceId,
            ILogger logger,
            bool includeSubdirectories)
        {
            this.directoryKind = directoryKind;
            this.instanceId = instanceId;
            this.logger = logger;
            watcher = new FileSystemWatcher(directory, "*")
            {
                IncludeSubdirectories = includeSubdirectories,
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
            logger.LogInformation(
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
            Changed?.Invoke(this, new InstanceDirectoryChangedEventArgs(e.ChangeType.ToString(), e.FullPath));
        }

        private void Watcher_Renamed(object sender, RenamedEventArgs e)
        {
            Changed?.Invoke(this, new InstanceDirectoryChangedEventArgs("Renamed", e.FullPath, e.OldFullPath));
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
}
