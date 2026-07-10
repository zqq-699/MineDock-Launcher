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

using Launcher.App.Resources;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Settings;

public sealed class SettingsJavaRuntimeItem
{
    public SettingsJavaRuntimeItem(JavaRuntimeInfo runtime)
    {
        DisplayName = runtime.DisplayName;
        VersionText = string.IsNullOrWhiteSpace(runtime.Version)
            ? Strings.Settings_JavaVersionUnknown
            : runtime.Version;
        MajorVersion = runtime.MajorVersion;
        Architecture = runtime.Architecture;
        ExecutablePath = runtime.ExecutablePath;
        InstallationDirectory = runtime.InstallationDirectory;
        Source = runtime.Source;
    }

    public string DisplayName { get; }

    public string VersionText { get; }

    public int? MajorVersion { get; }

    public string Architecture { get; }

    public string ExecutablePath { get; }

    public string InstallationDirectory { get; }

    public string Source { get; }
}
