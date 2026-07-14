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

namespace Launcher.Domain.Models;

public static class LaunchProgressStages
{
    public const string CheckingInstance = "Launch.CheckingInstance";
    public const string RepairingMetadata = "Launch.RepairingMetadata";
    public const string RepairingLoaderInstaller = "Launch.RepairingLoaderInstaller";
    public const string RunningLoaderInstaller = "Launch.RunningLoaderInstaller";
    public const string RepairingJar = "Launch.RepairingJar";
    public const string RepairingLibraries = "Launch.RepairingLibraries";
    public const string RepairingAssets = "Launch.RepairingAssets";
    public const string RepairingLogging = "Launch.RepairingLogging";
    public const string CheckingJava = "Launch.CheckingJava";
    public const string RunningPreLaunchCommand = "Launch.RunningPreLaunchCommand";
    public const string PreparingProcess = "Launch.PreparingProcess";
    public const string StartingProcess = "Launch.StartingProcess";
    public const string CheckingFiles = "Files";
    public const string DownloadingFiles = "Bytes";
    public const string DownloadSpeed = "DownloadSpeed";
}
