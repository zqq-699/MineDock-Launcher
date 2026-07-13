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

namespace Launcher.Infrastructure.FileSystem;

public sealed class LauncherStateMonitor : ILauncherStateMonitor
{
    private FileSystemWatcher? minecraftParentWatcher;
    private FileSystemWatcher? minecraftVersionsWatcher;
    private FileSystemWatcher? dataDirectoryWatcher;

    public event EventHandler? StateChanged;

    public void Watch(LauncherSettings settings)
    {
        Stop();

        var minecraftDirectory = settings.MinecraftDirectory;
        var minecraftParent = Path.GetDirectoryName(minecraftDirectory);
        var minecraftFolderName = Path.GetFileName(minecraftDirectory);
        if (!string.IsNullOrWhiteSpace(minecraftParent)
            && !string.IsNullOrWhiteSpace(minecraftFolderName))
        {
            minecraftParentWatcher = TryCreateWatcher(
                minecraftParent,
                minecraftFolderName,
                includeSubdirectories: false);
        }

        var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
        minecraftVersionsWatcher = TryCreateWatcher(versionsDirectory, "*", includeSubdirectories: true);

        dataDirectoryWatcher = TryCreateWatcher(
            settings.DataDirectory,
            "settings.json",
            includeSubdirectories: false);
    }

    public void Stop()
    {
        minecraftParentWatcher?.Dispose();
        minecraftVersionsWatcher?.Dispose();
        dataDirectoryWatcher?.Dispose();
        minecraftParentWatcher = null;
        minecraftVersionsWatcher = null;
        dataDirectoryWatcher = null;
    }

    public void Dispose()
    {
        Stop();
    }

    private FileSystemWatcher? TryCreateWatcher(string path, string filter, bool includeSubdirectories)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return null;

        try
        {
            var watcher = new FileSystemWatcher(path, filter)
            {
                IncludeSubdirectories = includeSubdirectories,
                NotifyFilter = NotifyFilters.DirectoryName
                    | NotifyFilters.FileName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size
            };

            watcher.Changed += WatcherStateChanged;
            watcher.Created += WatcherStateChanged;
            watcher.Deleted += WatcherStateChanged;
            watcher.Renamed += WatcherStateRenamed;
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private void WatcherStateChanged(object sender, FileSystemEventArgs e)
    {
        if (!IsCurrentWatcher(sender))
            return;

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void WatcherStateRenamed(object sender, RenamedEventArgs e)
    {
        if (!IsCurrentWatcher(sender))
            return;

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool IsCurrentWatcher(object sender)
    {
        return ReferenceEquals(sender, minecraftParentWatcher)
            || ReferenceEquals(sender, minecraftVersionsWatcher)
            || ReferenceEquals(sender, dataDirectoryWatcher);
    }
}
