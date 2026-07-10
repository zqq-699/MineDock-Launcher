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

namespace Launcher.App.Utilities;

internal static class GameInstanceDisplayFormatter
{
    public static string GetName(GameInstance instance)
    {
        return string.IsNullOrWhiteSpace(instance.Name)
            ? GetVersionName(instance)
            : instance.Name;
    }

    public static string GetMinecraftVersion(GameInstance instance)
    {
        return string.IsNullOrWhiteSpace(instance.MinecraftVersion)
            ? Strings.GameSettings_UnknownMinecraftVersion
            : instance.MinecraftVersion;
    }

    public static string GetVersionName(GameInstance instance)
    {
        return string.IsNullOrWhiteSpace(instance.VersionName)
            ? instance.MinecraftVersion
            : instance.VersionName;
    }

    public static string GetLoaderLabel(LoaderKind loader)
    {
        return LoaderDisplayNameProvider.GetDisplayName(loader);
    }

    public static string GetSubtitle(GameInstance instance)
    {
        var minecraftVersion = GetMinecraftVersion(instance);
        var loaderLabel = GetLoaderLabel(instance.Loader);
        var loaderVersionDisplay = LoaderVersionDisplayFormatter.Format(instance.Loader, instance.LoaderVersion);

        return instance.Loader switch
        {
            LoaderKind.Vanilla => string.Format(Strings.GameSettings_InstanceSubtitleVanillaFormat, minecraftVersion),
            _ when !string.IsNullOrWhiteSpace(loaderVersionDisplay)
                => string.Format(
                    Strings.GameSettings_InstanceSubtitleLoaderFormat,
                    minecraftVersion,
                    loaderLabel,
                    loaderVersionDisplay),
            _ => string.Format(
                Strings.GameSettings_InstanceSubtitleLoaderWithoutVersionFormat,
                minecraftVersion,
                loaderLabel)
        };
    }
}
