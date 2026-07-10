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

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Launcher.Infrastructure.Minecraft;

internal static class LauncherVersionMetadata
{
    private const string LauncherPropertyName = "launcher";
    private const string MinecraftVersionPropertyName = "minecraftVersion";

    public static void Apply(JsonObject versionJson, string minecraftVersion)
    {
        if (string.IsNullOrWhiteSpace(minecraftVersion))
            return;

        versionJson[LauncherPropertyName] = new JsonObject
        {
            [MinecraftVersionPropertyName] = minecraftVersion
        };
    }

    public static string ReadMinecraftVersion(JsonElement root)
    {
        if (!root.TryGetProperty(LauncherPropertyName, out var launcher)
            || launcher.ValueKind is not JsonValueKind.Object)
        {
            return string.Empty;
        }

        return TryGetStringProperty(launcher, MinecraftVersionPropertyName);
    }

    private static string TryGetStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
               && property.ValueKind is JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }
}
