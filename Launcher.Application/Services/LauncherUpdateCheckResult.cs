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

namespace Launcher.Application.Services;

public sealed record LauncherUpdateCheckResult(
    bool IsUpdateAvailable,
    LauncherUpdateInfo? Update,
    string CurrentVersion,
    bool IsFailed,
    string? ErrorMessage)
{
    public static LauncherUpdateCheckResult Latest(string currentVersion)
    {
        return new LauncherUpdateCheckResult(false, null, currentVersion, false, null);
    }

    public static LauncherUpdateCheckResult Available(string currentVersion, LauncherUpdateInfo update)
    {
        return new LauncherUpdateCheckResult(true, update, currentVersion, false, null);
    }

    public static LauncherUpdateCheckResult Failed(string currentVersion, string? errorMessage = null)
    {
        return new LauncherUpdateCheckResult(false, null, currentVersion, true, errorMessage);
    }
}
