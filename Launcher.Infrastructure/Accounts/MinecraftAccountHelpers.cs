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

using CmlLib.Core.Auth.Microsoft.Sessions;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Accounts;

internal static class MinecraftAccountHelpers
{
    public static bool IsActiveState(string? state)
    {
        return string.Equals(state, "ACTIVE", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeUuid(string? uuid)
    {
        return uuid?.Replace("-", string.Empty, StringComparison.Ordinal) ?? string.Empty;
    }

    public static string? GetActiveSkinUrl(JEProfile profile)
    {
        var activeSkin = profile.Skins?
            .FirstOrDefault(skin => IsActiveState(skin.State)
                && !string.IsNullOrWhiteSpace(skin.Url));

        return activeSkin?.Url
            ?? profile.Skins?.FirstOrDefault(skin => !string.IsNullOrWhiteSpace(skin.Url))?.Url;
    }

    public static string? GetActiveSkinUrl(MinecraftProfileResponse profile)
    {
        var activeSkin = profile.Skins?
            .FirstOrDefault(skin => IsActiveState(skin.State) && !string.IsNullOrWhiteSpace(skin.Url));

        return activeSkin?.Url
            ?? profile.Skins?.FirstOrDefault(skin => !string.IsNullOrWhiteSpace(skin.Url))?.Url;
    }

    public static MinecraftSkinModel? GetActiveSkinModel(MinecraftProfileResponse profile)
    {
        var activeSkin = profile.Skins?
            .FirstOrDefault(skin => IsActiveState(skin.State) && !string.IsNullOrWhiteSpace(skin.Url))
            ?? profile.Skins?.FirstOrDefault(skin => !string.IsNullOrWhiteSpace(skin.Url));

        return activeSkin?.Variant?.ToLowerInvariant() switch
        {
            "slim" => MinecraftSkinModel.Slim,
            "classic" => MinecraftSkinModel.Classic,
            _ => null
        };
    }
}
